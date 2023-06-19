using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;

namespace ConvertGenerator
{
    [Generator]
    public class ConvertToGenerator : ConvertBase, IIncrementalGenerator
    {
        private static string[] _searchTypes = new[] { "ConvertGenerator.Attriutes.ConvertTo", "ConvertGenerator.Attriutes.ConvertToAttribute", "ConvertTo", "ConvertToAttribute" };
        private static string _attributeFullName = "ConvertGenerator.Attriutes.ConvertToAttribute";
        private static string _dllName = "ConvertGenerator";

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var data = context.SyntaxProvider
                .CreateSyntaxProvider(FilterClass, GetClassSymbol).WithComparer(CusCompare.Comparer)
                .Where(FilterAttribute).WithComparer(CusCompare.Comparer)
                .Select(Process);

            context.RegisterSourceOutput(data, OutputSource);
        }

        private static void OutputSource(SourceProductionContext ctx, (string typeName, string code) item)
        {
            var fileName = $"{item.typeName}.g.cs";
            var template = GetClassTemplate();

            ctx.AddSource(fileName
               , template.Replace("#code#", item.code));
        }

        private static (string typeName, string code) Process((INamedTypeSymbol type, string namespaceVal, ParentClass parentClass) data, CancellationToken cancelToken)
        {
            var (sourceType, nameSpace, parentClass) = data;
            var typeName = sourceType.Name.ToString();

            var attrs = sourceType.GetAttributes().Where(m => m.AttributeClass.ToString() == _attributeFullName);

            if (string.IsNullOrEmpty(nameSpace))
            {
                StringBuilder sb = GenerateMehod(sourceType, typeName, attrs, "");
                return (typeName, code: sb.ToString());
            }
            else
            {
                var codesb = new StringBuilder();
                codesb.AppendLine($"namespace {nameSpace}");
                codesb.AppendLine("{");

                StringBuilder sb = GenerateMehod(sourceType, typeName, attrs, new string('\t', parentClass.Depth() - 1));
                GenerateClass(parentClass, sb, codesb, "\t");

                codesb.AppendLine("}");
                return (typeName, code: codesb.ToString());
            }
        }

        private static StringBuilder GenerateMehod(INamedTypeSymbol sourceType, string typeName, System.Collections.Generic.IEnumerable<AttributeData> attrs, string prefx)
        {
            var sb = new StringBuilder();

            var methodTemp = $"{prefx}\t\tpublic #target# ConvertTo#targetname#()\r\n{prefx}\t\t{{\r\n#init#\r\n{prefx}\t\t}}";

            foreach (var attr in attrs)
            {
                var targetType = attr.ConstructorArguments[0].Value as ITypeSymbol;
                var sbt = new StringBuilder();

                var targetFullName = targetType.ToString();
                var targetName = targetType.Name.ToString();
                var sourceFullName = sourceType.ToString();
                var methodString = methodTemp
                    .Replace("#source#", typeName)
                    .Replace("#target#", targetFullName)
                    .Replace("#targetname#", targetName);

                ProcessInit(sbt, sourceType, targetType, 1, "this", "r", prefx + "\t\t\t");

                methodString = methodString
                    .Replace("#init#", sbt.ToString());

                sb.AppendLine(methodString);

                sb.AppendLine($"{prefx}\t\tpublic static List<{targetFullName}> ConvertListTo(IList<{sourceType}> items) => items.Select(p => p.ConvertTo{targetName}()).ToList();");

            }

            return sb;
        }

        private static void GenerateClass(ParentClass parentClass, StringBuilder sb, StringBuilder codesb, string prefix)
        {
            codesb.AppendLine($"{prefix}{parentClass.Modifiers} {parentClass.Keyword} {parentClass.Name}");
            codesb.AppendLine($"{prefix}{{");
            var t = parentClass.Child;
            if (t == null)
            {
                codesb.AppendLine(sb.ToString());
            }
            else
            {
                GenerateClass(t, sb, codesb, prefix + "\t");
            }
            codesb.AppendLine($"{prefix}}}");
        }

        private static bool FilterAttribute((INamedTypeSymbol sourceType, string namespaceVal, ParentClass parentClass) data)
        {
            var attrs = data.sourceType.GetAttributes().Where(m => m.AttributeClass.ToString() == _attributeFullName);

            if (!attrs.Any())
            {
                return false;
            }
            return true;
        }

        private static (INamedTypeSymbol sourceType, string namespaceVal, ParentClass parentClass) GetClassSymbol(GeneratorSyntaxContext ctx, CancellationToken cancelToken)
        {
            var cls = ctx.Node as ClassDeclarationSyntax;
            return (ctx.SemanticModel.GetDeclaredSymbol(cls), GetNamespace(cls), GetParentClasses(cls));
        }

        private static bool FilterClass(SyntaxNode n, CancellationToken cancelToken)
        {
            if (!(n is ClassDeclarationSyntax classDeclaration))
            {
                return false;
            }

            //必须标记特性ConvertTo
            var judge = classDeclaration.AttributeLists.SelectMany(m => m.Attributes).Any(m => _searchTypes.Contains(m.Name.ToString()));
            if (!judge)
            {
                return false;
            }

            //满足要求 
            return true;
        }

        /// <summary>
        /// init code
        /// </summary>
        /// <param name="sbt"></param>
        /// <param name="sourceProperties"></param>
        /// <param name="targetType"></param>
        /// <param name="targetFullName"></param>
        /// <param name="depth">最大深度，限定为12</param>
        private static void ProcessInit(StringBuilder sbt, ITypeSymbol sourceType, ITypeSymbol targetType, int depth, string item, string r, string prefix)
        {
            var targetFullName = targetType.ToString().TrimEnd('?');
            if (depth > 12)
            {
                sbt.AppendLine($"{prefix}if({item} != null) \r\n{prefix}{r} = new {targetFullName}();");
                return;
            }
            var sourceProperties = GetInstancePublicPropertyGet(sourceType);
            sbt.AppendLine($"{prefix}if({item} != null) \r\n{prefix}{{\r\n{prefix}\t{(depth == 1 ? "var " : "")}{r} = new {targetFullName}();");
            prefix += "\t";
            var targetPs = GetInstancePublicPropertySet(targetType);
            foreach (IPropertySymbol targetP in targetPs)
            {
                var sourceP = sourceProperties.FirstOrDefault(m => m.Name.ToLower() == targetP.Name.ToLower()) as IPropertySymbol;
                if (sourceP == null)
                {
                    continue;
                }
                //Dictionary类型特殊处理，泛型类型一样，只做浅拷贝
                if (targetP.Type.Name == "Dictionary" && targetP.Type is INamedTypeSymbol targetDic
                    && sourceP.Type.Name == "Dictionary" && sourceP.Type is INamedTypeSymbol sourceDic)
                {
                    var ttypeKey = targetDic.TypeArguments[0];
                    var stypeKey = sourceDic.TypeArguments[0];
                    var ttypeVal = targetDic.TypeArguments[1];
                    var stypeVal = sourceDic.TypeArguments[1];
                    if (SymbolEqualityComparer.Default.Equals(stypeKey, ttypeKey)
                        && SymbolEqualityComparer.Default.Equals(stypeVal, ttypeVal))
                    {
                        sbt.AppendLine($"{prefix}if({item}.{sourceP.Name} != null) {r}.{targetP.Name} = {item}.{sourceP.Name};");
                    }
                    else
                    {
                        var sbvalue = new StringBuilder();
                        ProcessInit(sbvalue, stypeVal, ttypeVal, 1, "b.Value", $"c{depth + 1}", $"{prefix}\t");

                        sbt.AppendLine($"{prefix}if({item}.{sourceP.Name} != null) {r}.{targetP.Name} = {item}.{sourceP.Name}.ToDictionary(a => a.Key, b =>  \r\n{prefix}{{\r\n{sbvalue}{prefix}}});");
                    }
                }
                //List类型特殊处理，泛型类型一样，只做浅拷贝
                else if ((targetP.Type.Name == "List" || targetP.Type.Name == "RepeatedField") && targetP.Type is INamedTypeSymbol targetList
                    && (sourceP.Type.Name == "List" || sourceP.Type.Name == "RepeatedField") && sourceP.Type is INamedTypeSymbol sourceList)
                {
                    var ttype = targetList.TypeArguments[0];
                    var stype = sourceList.TypeArguments[0];
                    if (SymbolEqualityComparer.Default.Equals(stype, ttype))
                    {
                        if (sourceP.Type.Name != targetP.Type.Name)
                        {
                            if (targetP.Type.Name == "RepeatedField")
                            {
                                sbt.AppendLine($"{prefix}if({item}.{sourceP.Name} != null) {r}.{targetP.Name}.Add({item}.{sourceP.Name});");
                            }
                            else
                            {
                                sbt.AppendLine($"{prefix}if({item}.{sourceP.Name} != null) {r}.{targetP.Name} = {item}.{sourceP.Name}.ToList();");
                            }
                        }
                        else
                        {
                            sbt.AppendLine($"{prefix}if({item}.{sourceP.Name} != null) {r}.{targetP.Name} = {item}.{sourceP.Name};");
                        }
                    }
                    else
                    {
                        var sbls = new StringBuilder();
                        ProcessInit(sbls, stype, ttype, 1, "a", $"b{depth + 1}", $"{prefix}\t");
                        if (targetP.Type.Name == "RepeatedField")
                        {
                            sbt.AppendLine($"{prefix}if({item}.{sourceP.Name} != null) {r}.{targetP.Name}.Add({item}.{sourceP.Name}.Select(a => \r\n{prefix}{{\r\n{sbls}{prefix}}}).ToList());");
                        }
                        else
                        {
                            sbt.AppendLine($"{prefix}if({item}.{sourceP.Name} != null) {r}.{targetP.Name} = {item}.{sourceP.Name}.Select(a => \r\n{prefix}{{\r\n{sbls}{prefix}}}).ToList();");
                        }
                    }
                }
                else
                {
                    if (SymbolEqualityComparer.Default.Equals(sourceP.Type, targetP.Type))
                    {
                        sbt.AppendLine($"{prefix}{r}.{targetP.Name} = {item}.{sourceP.Name};");
                    }
                    else if (IsBothDateTime(sourceP.Type, targetP.Type))
                    {
                        if (targetP.Type.ToString() == "Google.Protobuf.WellKnownTypes.Timestamp")
                        {
                            if (sourceP.Type.ToString() == "System.DateTime")
                            {
                                sbt.AppendLine($"{prefix}{r}.{targetP.Name} = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime({item}.{sourceP.Name}.ToUniversalTime());");
                            }
                            else if (sourceP.Type.ToString() == "System.DateTimeOffset")
                            {
                                sbt.AppendLine($"{prefix}{r}.{targetP.Name} = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTimeOffset({item}.{sourceP.Name});");
                            }
                        }
                        else
                        {
                            if (targetP.Type.ToString() == "System.DateTime")
                            {
                                if (sourceP.Type.ToString() == "Google.Protobuf.WellKnownTypes.Timestamp")
                                {
                                    sbt.AppendLine($"{prefix}{r}.{targetP.Name} = {item}.{sourceP.Name}.ToDateTime().ToLocalTime();");
                                }
                                else
                                {
                                    sbt.AppendLine($"{prefix}{r}.{targetP.Name} = {item}.{sourceP.Name}.DateTime;");
                                }
                            }
                            else if (targetP.Type.ToString() == "System.DateTimeOffset")
                            {
                                if (sourceP.Type.ToString() == "Google.Protobuf.WellKnownTypes.Timestamp")
                                {
                                    sbt.AppendLine($"{prefix}{r}.{targetP.Name} = {item}.{sourceP.Name}.ToDateTimeOffset().ToLocalTime();");
                                }
                                else
                                {
                                    sbt.AppendLine($"{prefix}{r}.{targetP.Name} = {item}.{sourceP.Name};");
                                }
                            }
                        }
                    }
                    else if (IsBothTimeSpan(sourceP.Type, targetP.Type))
                    {
                        if (targetP.Type.ToString() == "Google.Protobuf.WellKnownTypes.Duration")
                        {
                            sbt.AppendLine($"{prefix}{r}.{targetP.Name} = Google.Protobuf.WellKnownTypes.Duration.FromTimeSpan({item}.{sourceP.Name});");
                        }
                        else
                        {
                            sbt.AppendLine($"{prefix}{r}.{targetP.Name} = {item}.{sourceP.Name}.ToTimeSpan();");
                        }
                    }

                    //判断该属性，是否包含公共、属性、实例、set 
                    else if (GetInstancePublicPropertySet(sourceP.Type).Any())
                    {
                        ProcessInit(sbt, sourceP.Type, targetP.Type, depth + 1, $"{item}.{sourceP.Name}", $"{r}.{targetP.Name}", prefix);
                    }
                }
            }
            if (depth == 1)
            {
                sbt.AppendLine($"{prefix}return {r};");
                sbt.AppendLine($"{prefix.Substring(0, prefix.Length - 1)}}} else return null;");
            }
            else
            {
                sbt.AppendLine($"{prefix.Substring(0, prefix.Length - 1)}}}");
            }
        }

        private static string _classTemplate = string.Empty;
        private static string GetClassTemplate()
        {
            if (!string.IsNullOrWhiteSpace(_classTemplate))
            {
                return _classTemplate;
            }
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"{_dllName}.Templates.ConvertToTemplate.txt"))
            using (StreamReader sr = new StreamReader(stream, Encoding.UTF8))
            {
                _classTemplate = sr.ReadToEnd();
            }

            return _classTemplate;
        }
    }
}
