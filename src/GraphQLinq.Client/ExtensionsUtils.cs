using System;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;

namespace GraphQLinq
{
    static class ExtensionsUtils
    {
        internal static bool IsValueTypeOrString(this Type type)
        {
            return type.IsValueType || type == typeof(string);
        }

        internal static bool IsList(this Type type)
        {
            return type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>);
        }

        internal static bool HasNestedProperties(this Type type)
        {
            return !IsValueTypeOrString(type);
        }

        internal static Type GetTypeOrListType(this Type type)
        {
            if (type.IsList())
            {
                var genericArguments = type.GetGenericArguments();

                return genericArguments[0].GetTypeOrListType();
            }

            return type;
        }

        internal static Expression RemoveConvert(this Expression expression)
        {
            while ((expression != null)
                   && (expression.NodeType == ExpressionType.Convert
                       || expression.NodeType == ExpressionType.ConvertChecked))
            {
                expression = RemoveConvert(((UnaryExpression)expression).Operand);
            }

            return expression;
        }

        internal static string ToCamelCase(this string input)
        {
            if (char.IsLower(input[0]))
            {
                return input;
            }
            return input.Substring(0, 1).ToLower() + input.Substring(1);
        }

        //doesn't apply camelCase if name is only uppercase
        internal static string ToCamelCase(this string input, bool ignoreUpperCase)
        {
            return ignoreUpperCase && input.IsUpperCase() ? input : input.ToCamelCase();
        }

        internal static bool IsUpperCase(this string input)
        {
            foreach(char c in input)
            {
                if(Char.IsLower(c))
                    return false;
            }
            return true;
        }

        internal static string ToGraphQlType(this Type type)
        {
            if (type == typeof(bool))
            {
                return "Boolean";
            }

            if (type == typeof(int))
            {
                return "Int";
            }

            if (type == typeof(string))
            {
                return "String";
            }

            if (type == typeof(float))
            {
                return "Float";
            }

            if (type.IsList())
            {
                var listType = type.GetTypeOrListType();
                return "[" + ToGraphQlType(listType) + "]";
            }

            return type.Name;
        }
    }
}