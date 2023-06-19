using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;

namespace ConvertGenerator
{
    [Generator]
    public class ConvertFromGenerator : ConvertBase, IIncrementalGenerator
    {
        private static string[] _searchTypes = new[] { "ConvertGenerator.Attriutes.ConvertFrom", "ConvertGenerator.Attriutes.ConvertFromAttribute", "ConvertFrom", "ConvertFromAttribute" };
        private static string _attributeFullName = "ConvertGenerator.Attriutes.ConvertFromAttribute";
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
            cancelToken.ThrowIfCancellationRequested();

            var (sourceType, nameSpace, parentClass) = data;

            var typeName = sourceType.Name.ToString();

            var attrs = sourceType.GetAttributes().Where(m => m.AttributeClass.ToString() == _attributeFullName);

            if (string.IsNullOrEmpty(nameSpace))
            {
                var codesb = new StringBuilder();

                StringBuilder sb = GenerateMehod(sourceType, typeName, attrs, new string('\t', parentClass.Depth() - 1));
                GenerateClass(parentClass, sb, codesb, "\t");

                return (typeName, code: codesb.ToString());
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

        private static StringBuilder GenerateMehod(INamedTypeSymbol sourceType, string typeName, IEnumerable<AttributeData> attrs, string prefix)
        {
            var sb = new StringBuilder();

            var methodTemp = $"{prefix}\t\tpublic static #source# ConvertFrom(#target# item)\r\n{prefix}\t\t{{\r\n#init#\r\n{prefix}\t\t}}";

            foreach (var attr in attrs)
            {
                var targetType = attr.ConstructorArguments[0].Value as ITypeSymbol;
                var sbt = new StringBuilder();

                var targetFullName = targetType.ToString();
                var methodString = methodTemp
                    .Replace("#source#", typeName)
                    .Replace("#target#", targetFullName);

                ProcessInit(sbt, sourceType, targetType, 1, "item", "r", prefix + "\t\t\t");

                methodString = methodString
                    .Replace("#init#", sbt.ToString());

                sb.AppendLine(methodString);

                sb.AppendLine($"{prefix}\t\tpublic static List<{typeName}> ConvertListFrom(IList<{targetType}> items) => items.Select(p => {typeName}.ConvertFrom(p)).ToList();");
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

            //必须标记特性ConvertFrom
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
        /// <param name="sourceFullName"></param>
        /// <param name="depth">最大深度，限定为12</param>
        private static void ProcessInit(StringBuilder sbt, ITypeSymbol sourceType, ITypeSymbol targetType, int depth, string item, string r, string prefix)
        {
            var sourceFullName = sourceType.ToString().TrimEnd('?');
            if (depth > 12)
            {
                sbt.AppendLine($"{prefix}if({item} != null) \r\n{prefix}{r} = new {sourceFullName}();");
                return;
            }
            var sourceProperties = GetInstancePublicPropertySet(sourceType);
            sbt.AppendLine($"{prefix}if({item} != null) \r\n{prefix}{{\r\n{prefix}\t{(depth == 1 ? "var " : "")}{r} = new {sourceFullName}();");
            prefix += "\t";
            var targetPs = GetInstancePublicPropertyGet(targetType);
            foreach (IPropertySymbol sourceP in sourceProperties)
            {
                var targetP = targetPs.FirstOrDefault(m => m.Name.ToLower() == sourceP.Name.ToLower());
                if (targetP == null)
                {
                    continue;
                }
                //Dictionary类型特殊处理，泛型类型一样，只做浅拷贝
                if (sourceP.Type.Name == "Dictionary" && sourceP.Type is INamedTypeSymbol sourceDic
                    && targetP.Type.Name == "Dictionary" && targetP.Type is INamedTypeSymbol targetDic)
                {
                    var stypeKey = sourceDic.TypeArguments[0];
                    var ttypeKey = targetDic.TypeArguments[0];
                    var stypeVal = sourceDic.TypeArguments[1];
                    var ttypeVal = targetDic.TypeArguments[1];
                    if (SymbolEqualityComparer.Default.Equals(stypeKey, ttypeKey)
                        && SymbolEqualityComparer.Default.Equals(stypeVal, ttypeVal))
                    {
                        sbt.AppendLine($"{prefix}if({item}.{targetP.Name} != null) {r}.{sourceP.Name} = {item}.{targetP.Name};");
                    }
                    else
                    {
                        var sbvalue = new StringBuilder();
                        ProcessInit(sbvalue, stypeVal, ttypeVal, 1, "b.Value", $"c{depth + 1}", $"{prefix}\t");

                        sbt.AppendLine($"{prefix}if({item}.{targetP.Name} != null) {r}.{sourceP.Name} = {item}.{targetP.Name}.ToDictionary(a => a.Key, b => \r\n{prefix}{{\r\n{sbvalue}{prefix}}});");
                    }
                }
                //List类型特殊处理，泛型类型一样，只做浅拷贝
                else if ((sourceP.Type.Name == "List" || sourceP.Type.Name == "RepeatedField") && sourceP.Type is INamedTypeSymbol sourceList
                    && (targetP.Type.Name == "List" || targetP.Type.Name == "RepeatedField") && targetP.Type is INamedTypeSymbol targetList)
                {
                    var stype = sourceList.TypeArguments[0];
                    var ttype = targetList.TypeArguments[0];
                    if (SymbolEqualityComparer.Default.Equals(stype, ttype))
                    {
                        if (sourceP.Type.Name != targetP.Type.Name)
                        {
                            if (sourceP.Type.Name == "RepeatedField")
                            {
                                sbt.AppendLine($"{prefix}if({item}.{targetP.Name} != null) {r}.{sourceP.Name}.Add({item}.{targetP.Name});");
                            }
                            else
                            {
                                sbt.AppendLine($"{prefix}if({item}.{targetP.Name} != null) {r}.{sourceP.Name} = {item}.{targetP.Name}.ToList();");
                            }
                        }
                        else
                        {
                            sbt.AppendLine($"{prefix}if({item}.{targetP.Name} != null) {r}.{sourceP.Name} = {item}.{targetP.Name};");
                        }
                    }
                    else
                    {
                        var sbls = new StringBuilder();
                        ProcessInit(sbls, stype, ttype, 1, "a", $"b{depth + 1}", $"{prefix}\t");
                        if (sourceP.Type.Name == "RepeatedField")
                        {
                            sbt.AppendLine($"{prefix}if({item}.{targetP.Name} != null) {r}.{sourceP.Name}.Add({item}.{targetP.Name}.Select(a => \r\n{prefix}{{\r\n{sbls}{prefix}}}).ToList());");
                        }
                        else
                        {
                            sbt.AppendLine($"{prefix}if({item}.{targetP.Name} != null) {r}.{sourceP.Name} = {item}.{targetP.Name}.Select(a => \r\n{prefix}{{\r\n{sbls}{prefix}}}).ToList();");
                        }
                    }
                }
                else
                {
                    if (SymbolEqualityComparer.Default.Equals(sourceP.Type, targetP.Type))
                    {
                        sbt.AppendLine($"{prefix}{r}.{sourceP.Name} = {item}.{targetP.Name};");
                    }
                    else if (IsBothDateTime(sourceP.Type, targetP.Type))
                    {
                        if (sourceP.Type.ToString() == "Google.Protobuf.WellKnownTypes.Timestamp")
                        {
                            if (targetP.Type.ToString() == "System.DateTime")
                            {
                                sbt.AppendLine($"{prefix}{r}.{sourceP.Name} = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTime({item}.{targetP.Name}.ToUniversalTime());");
                            }
                            else if (targetP.Type.ToString() == "System.DateTimeOffset")
                            {
                                sbt.AppendLine($"{prefix}{r}.{sourceP.Name} = Google.Protobuf.WellKnownTypes.Timestamp.FromDateTimeOffset({item}.{targetP.Name});");
                            }
                        }
                        else if (sourceP.Type.ToString() == "System.DateTime")
                        {
                            if (targetP.Type.ToString() == "Google.Protobuf.WellKnownTypes.Timestamp")
                            {
                                sbt.AppendLine($"{prefix}{r}.{sourceP.Name} = {item}.{targetP.Name}.ToDateTime().ToLocalTime();");
                            }
                            else
                            {
                                sbt.AppendLine($"{prefix}{r}.{sourceP.Name} = {item}.{targetP.Name}.DateTime;");
                            }
                        }
                        else if (sourceP.Type.ToString() == "System.DateTimeOffset")
                        {
                            if (targetP.Type.ToString() == "Google.Protobuf.WellKnownTypes.Timestamp")
                            {
                                sbt.AppendLine($"{prefix}{r}.{sourceP.Name} = {item}.{targetP.Name}.ToDateTimeOffset().ToLocalTime();");
                            }
                            else
                            {
                                sbt.AppendLine($"{prefix}{r}.{sourceP.Name} = {item}.{targetP.Name};");
                            }
                        }
                    }
                    else if (IsBothTimeSpan(sourceP.Type, targetP.Type))
                    {
                        if (sourceP.Type.ToString() == "Google.Protobuf.WellKnownTypes.Duration")
                        {
                            sbt.AppendLine($"{prefix}{r}.{sourceP.Name} = Google.Protobuf.WellKnownTypes.Duration.FromTimeSpan({item}.{targetP.Name});");
                        }
                        else
                        {
                            sbt.AppendLine($"{prefix}{r}.{sourceP.Name} = {item}.{targetP.Name}.ToTimeSpan();");
                        }
                    }
                    //判断该属性，是否包含公共、属性、实例、set 
                    else if (GetInstancePublicPropertySet(sourceP.Type).Any())
                    {
                        ProcessInit(sbt, sourceP.Type, targetP.Type, depth + 1, $"{item}.{targetP.Name}", $"{r}.{sourceP.Name}", prefix);
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

            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream($"{_dllName}.Templates.ConvertFromTemplate.txt"))
            using (StreamReader sr = new StreamReader(stream, Encoding.UTF8))
            {
                _classTemplate = sr.ReadToEnd();
            }
            return _classTemplate;
        }
    }
}
