using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ConvertGenerator
{
    public class ConvertBase
    {
        static string[] _dateTimeType = { "System.DateTimeOffset", "System.DateTime", "Google.Protobuf.WellKnownTypes.Timestamp" };
        static string[] _timeSpanType = { "System.TimeSpan", "Google.Protobuf.WellKnownTypes.Duration" };

        public static bool IsBothDateTime(ITypeSymbol type1, ITypeSymbol type2)
        {
            var t1 = type1.ToString();
            var t2 = type2.ToString();

            if (_dateTimeType.Contains(t1) && _dateTimeType.Contains(t2))
            {
                return true;
            }

            return false;
        }
        public static bool IsBothTimeSpan(ITypeSymbol type1, ITypeSymbol type2)
        {
            var t1 = type1.ToString();
            var t2 = type2.ToString();


            if (_timeSpanType.Contains(t1) && _timeSpanType.Contains(t2))
            {
                return true;
            }

            return false;
        }

        public static IEnumerable<IPropertySymbol> GetInstancePublicPropertySet(ITypeSymbol type)
        {
            var ls = new List<IPropertySymbol>();
            //只处理公共、属性、实例、set，包含基类，包括grpc的RepeatedField
            var t = type;
            do
            {
                ls.AddRange(t.GetMembers().Where(m =>
                       m.Kind == SymbolKind.Property
                       && !m.IsStatic
                       && m.DeclaredAccessibility == Accessibility.Public
                       &&
                       (
                            (m as IPropertySymbol)?.SetMethod != null && (m as IPropertySymbol)?.SetMethod.DeclaredAccessibility == Accessibility.Public
                            || ((m as IPropertySymbol).Type.Name == "RepeatedField")
                       )
                    )
                   .OfType<IPropertySymbol>()
                   .ToList());
                if (t.BaseType == null)
                {
                    break;
                }
                t = t.BaseType;
            } while (true);
            return ls;
        }

        public static IEnumerable<IPropertySymbol> GetInstancePublicPropertyGet(ITypeSymbol type)
        {
            var ls = new List<IPropertySymbol>();

            //只处理公共、属性、实例、get，包含基类
            var t = type;
            do
            {
                ls.AddRange(t.GetMembers().Where(m =>
                        m.Kind == SymbolKind.Property
                        && !m.IsStatic
                        && m.DeclaredAccessibility == Accessibility.Public
                        && (m as IPropertySymbol)?.GetMethod != null
                        && (m as IPropertySymbol)?.GetMethod.DeclaredAccessibility == Accessibility.Public)
                    .OfType<IPropertySymbol>()
                    .ToArray());
                if (t.BaseType == null)
                {
                    break;
                }
                t = t.BaseType;
            } while (true);
            return ls;
        }

        public static string GetNamespace(BaseTypeDeclarationSyntax syntax)
        {
            string nameSpace = string.Empty;

            SyntaxNode potentialNamespaceParent = syntax.Parent;

            while (potentialNamespaceParent != null &&
                    !(potentialNamespaceParent is NamespaceDeclarationSyntax)
                    && !(potentialNamespaceParent is FileScopedNamespaceDeclarationSyntax))
            {
                potentialNamespaceParent = potentialNamespaceParent.Parent;
            }

            if (potentialNamespaceParent is BaseNamespaceDeclarationSyntax namespaceParent)
            {
                nameSpace = namespaceParent.Name.ToString();

                while (true)
                {
                    if (!(namespaceParent.Parent is NamespaceDeclarationSyntax parent))
                    {
                        break;
                    }

                    nameSpace = $"{namespaceParent.Name}.{nameSpace}";
                    namespaceParent = parent;
                }
            }

            return nameSpace;
        }

        public static ParentClass GetParentClasses(TypeDeclarationSyntax typeSyntax)
        {
            ParentClass parentClassInfo = new ParentClass(
                    keyword: typeSyntax.Keyword.ValueText,
                    name: typeSyntax.Identifier.ToString() + typeSyntax.TypeParameterList,
                    modifiers: typeSyntax.Modifiers.ToString(),
                    child: null);

            TypeDeclarationSyntax parentSyntax = typeSyntax.Parent as TypeDeclarationSyntax;

            while (parentSyntax != null && IsAllowedKind(parentSyntax.Kind()))
            {
                parentClassInfo = new ParentClass(
                    keyword: parentSyntax.Keyword.ValueText,
                    name: parentSyntax.Identifier.ToString() + parentSyntax.TypeParameterList,
                    modifiers: parentSyntax.Modifiers.ToString(),
                    child: parentClassInfo);

                parentSyntax = (parentSyntax.Parent as TypeDeclarationSyntax);
            }

            return parentClassInfo;

        }

        static bool IsAllowedKind(SyntaxKind kind) =>
            kind == SyntaxKind.ClassDeclaration ||
            kind == SyntaxKind.StructDeclaration ||
            kind == SyntaxKind.RecordDeclaration;
    }

    public class ParentClass
    {
        public static readonly ParentClassEqualCompare Comparer = new ParentClassEqualCompare();
        public ParentClass(string keyword, string name, string modifiers, ParentClass child)
        {
            Keyword = keyword;
            Name = name;
            Modifiers = modifiers;
            Child = child;
        }

        public ParentClass Child { get; }
        public string Keyword { get; }
        public string Name { get; }
        public string Modifiers { get; }

        public override string ToString()
        {
            string val = "";
            ParentClass item = this;

            while (item != null)
            {
                val += $"{item.Keyword}.{item.Name}.{item.Modifiers}";
                item = item.Child;
            }
            return val;
        }

        public int Depth()
        {
            int i = 0;
            var val = this;
            while (true)
            {
                i++;
                val = val.Child;
                if (val == null)
                {
                    break;
                }
            }
            return i;
        }
    }

    public class ParentClassEqualCompare : IEqualityComparer<ParentClass>
    {
        public bool Equals(ParentClass x, ParentClass y)
        {
            if (!(x.Keyword == y.Keyword && x.Name == y.Name && x.Modifiers == y.Modifiers))
            {
                return false;
            }
            if (x.Child == y.Child && x.Child == null)
            {
                return true;
            }
            if (x.Child != null && y.Child != null)
            {
                return Equals(x.Child, y.Child);
            }

            return false;
        }

        public int GetHashCode(ParentClass obj)
        {
            return obj.ToString().GetHashCode();
        }
    }

    internal class CusCompare : IEqualityComparer<(INamedTypeSymbol sourceType, string namespaceVal, ParentClass parentClass)>
    {
        public static readonly CusCompare Comparer = new CusCompare();
        public bool Equals((INamedTypeSymbol sourceType, string namespaceVal, ParentClass parentClass) x, (INamedTypeSymbol sourceType, string namespaceVal, ParentClass parentClass) y)
        {
            return SymbolEqualityComparer.Default.Equals(x.sourceType, y.sourceType)
                && x.namespaceVal == y.namespaceVal
                && ParentClass.Comparer.Equals(x.parentClass, y.parentClass);
        }

        public int GetHashCode((INamedTypeSymbol sourceType, string namespaceVal, ParentClass parentClass) obj)
        {
            return (obj.sourceType.ToString() + obj.namespaceVal + obj.parentClass.ToString()).GetHashCode();
        }
    }
}
