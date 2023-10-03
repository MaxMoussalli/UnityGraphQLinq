using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Linq.Expressions;
using System.Reflection;
using Newtonsoft.Json;

namespace GraphQLinq
{
    class GraphQueryBuilder<T>
    {
        private const string QueryTemplate = @"{0} {1} {{ {2}: {3} {4} {{ {5} }} }}";
        private const string ScalarQueryTemplate = @"{0} {1} {{ {2}: {3} {4} {5} }}";

        internal const string ResultAlias = "result";

        public GraphQLQuery BuildQuery(GraphQuery<T> graphQuery, List<IncludeDetails> includes)
        {
            var selectClause = "";

            var passedArguments = graphQuery.Arguments.Where(pair => pair.Value != null).ToList();
            var queryVariables = passedArguments.ToDictionary(pair => pair.Key, pair => pair.Value);

            bool hasSelector = graphQuery.Selector != null;
            if (hasSelector)
            {
                var body = graphQuery.Selector.Body;

                var padding = new string(' ', 4);

                var fields = new List<string>();

                switch (body)
                {
                    case MemberExpression memberExpression:
                        var member = memberExpression.Member;
                        selectClause = BuildMemberAccessSelectClause(body, selectClause, padding);
                        break;

                    case NewExpression newExpression:
                        foreach (var argument in newExpression.Arguments.OfType<MemberExpression>())
                        {
                            var selectField = BuildMemberAccessSelectClause(argument, selectClause, padding);
                            fields.Add(selectField);
                        }
                        selectClause = string.Join(Environment.NewLine, fields);
                        break;

                    case MemberInitExpression memberInitExpression:
                        foreach (var argument in memberInitExpression.Bindings.OfType<MemberAssignment>())
                        {
                            var selectField = BuildMemberAccessSelectClause(argument.Expression, selectClause, padding);
                            fields.Add(selectField);
                        }
                        selectClause = string.Join(Environment.NewLine, fields);
                        break;
                    default:
                        throw new NotSupportedException($"Selector of type {body.NodeType} is not implemented yet");
                }
            }

            // Handle include clauses
            var select = BuildSelectClauseForType(typeof(T), includes, !hasSelector, includeAll: graphQuery.IncludeAllSetting);
            selectClause += select.SelectClause;
            foreach (var item in select.IncludeArguments)
            {
                queryVariables.Add(item.Key, item.Value);
            }

            var isScalarQuery = string.IsNullOrEmpty(selectClause);
            selectClause = Environment.NewLine + selectClause + Environment.NewLine;

            var queryParameters = passedArguments.Any() ? $"({string.Join(", ", passedArguments.Select(pair => $"{pair.Key}: ${pair.Key}"))})" : "";
            var args = queryVariables.Select(pair => "$" + pair.Key + ": " + graphQuery.Context.GetArgsDefinition(graphQuery.QueryName, pair.Key));
            var queryParameterTypes = queryVariables.Any() ? $"({string.Join(", ", args)})" : "";

            string queryFormat = isScalarQuery ? ScalarQueryTemplate : QueryTemplate;
            var graphQLQuery = string.Format(queryFormat, graphQuery.Context.QueryKeyword, queryParameterTypes, ResultAlias, 
                                                          graphQuery.QueryName, queryParameters, selectClause);

            var dictionary = new Dictionary<string, object> { { "query", graphQLQuery }, { "variables", queryVariables } };

            // [MM] XK - Replace System.Text.Json by Newtonsoft.Json to make plugin compatible with Unity
            var json = JsonConvert.SerializeObject(dictionary, GraphContext.CreateJsonSerializerSettings());

            return new GraphQLQuery(graphQLQuery, queryVariables, json);
        }

        private static string BuildMemberAccessSelectClause(Expression body, string selectClause, string padding, string alias = null)
        {
            if (body is MemberExpression memberExpression)
            {
                var member = memberExpression.Member as PropertyInfo;

                // ignore members with GraphQLIgnore attribute
                var toIgnore = member.GetCustomAttribute<GraphQLinqIgnoreAttribute>() != null;

                if (member != null && !toIgnore)
                {
                    if (string.IsNullOrEmpty(selectClause))
                    {
                        var aliasStr = string.IsNullOrEmpty(alias) ? "" : $"{alias}: ";
                        selectClause = $"{padding}{aliasStr}{member.Name.ToCamelCase()}";

                        if (!member.PropertyType.GetTypeOrListType().IsValueTypeOrString())
                        {
                            var fieldForProperty = BuildSelectClauseForType(member.PropertyType.GetTypeOrListType(), 3);
                            selectClause = $"{selectClause} {{{Environment.NewLine}{fieldForProperty}{Environment.NewLine}{padding}}}";
                        }
                    }
                    else
                    {
                        selectClause = $"{member.Name.ToCamelCase()} {{ {Environment.NewLine}{selectClause}}}";
                    }
                    return BuildMemberAccessSelectClause(memberExpression.Expression, selectClause, padding);
                }
                return selectClause;
            }
            return selectClause;
        }

        private static string BuildSelectClauseForType(Type targetType, int depth = 1, bool includeAll = false)
        {
            if (targetType == typeof(string))
                return "";

            var propertyInfos = targetType.GetProperties()
                .Where(info => info.GetCustomAttribute<GraphQLinqIgnoreAttribute>() == null); // ignore properties with GraphQLIgnore attribute;
                                        
            var flattenProperties = propertyInfos
                .Where(info => !info.PropertyType.HasNestedProperties());      //remove nested properties

            var padding = new string(' ', depth * 2);

            string sumNestedSelectClause = "";
            if (includeAll)
            {
                var nestedProperties = propertyInfos.Where(info => info.PropertyType.HasNestedProperties());
                List<string> propertySerialized = new List<string>(nestedProperties.Count());

                //recursivity on nested properties
                foreach (var propertyInfo in nestedProperties)
                {
                    Type propertyType = propertyInfo.PropertyType;

                    //if type is Collection.generic, consider generic argument type instead of collection type
                    if (propertyType.IsGenericType)
                    {
                        propertyType = propertyType.GetGenericArguments()[0];
                    }

                    //if type is array, consider element type instead of array type
                    if (propertyType.IsArray)
                    {
                        propertyType = propertyType.GetElementType();
                    }


                    if (!propertyType.HasNestedProperties()) // if type hasn't nest properties, add it in flattenProperties
                    {
                        flattenProperties = flattenProperties.Concat(new[] { propertyInfo });
                        continue;
                    }

                    var nestedSelectClause = BuildSelectClauseForType(propertyType, includeAll:true);
                    string formatedProperty = $"{padding}{propertyInfo.Name.ToCamelCase()} {{{Environment.NewLine} {nestedSelectClause}{Environment.NewLine}{padding}}}";
                    propertySerialized.Add(formatedProperty);
                }

                //Concatenation of each nested properties of the same level
                sumNestedSelectClause = string.Join(Environment.NewLine, propertySerialized);
            }

            var selectClause = string.Join(Environment.NewLine, 
                    flattenProperties.Select(info => $"{padding}{info.Name.ToCamelCase()}"));

            if(sumNestedSelectClause != "")
                selectClause = selectClause + Environment.NewLine + padding + sumNestedSelectClause;

            return selectClause;
        }

        private static SelectClauseDetails BuildSelectClauseForType(Type targetType, List<IncludeDetails> includes, bool includeDefaultSelect = true, bool includeAll = false)
        {
            var selectClause = includeDefaultSelect ? BuildSelectClauseForType(targetType, includeAll: includeAll) : "";
            var includeVariables = new Dictionary<string, object>();

            for (var index = 0; index < includes.Count; index++)
            {
                var include = includes[index];
                var prefix = includes.Count == 1 ? "" : index.ToString();

                var fieldsFromInclude = BuildSelectClauseForInclude(targetType, include, includeVariables, prefix);
                selectClause = selectClause + Environment.NewLine + fieldsFromInclude;
            }
            return new SelectClauseDetails { SelectClause = selectClause, IncludeArguments = includeVariables };
        }

        private static string BuildSelectClauseForInclude(Type targetType, IncludeDetails includeDetails, Dictionary<string, object> includeVariables, string parameterPrefix = "", int parameterIndex = 0, int depth = 1)
        {
            var include = includeDetails.Path;
            if (string.IsNullOrEmpty(include))
            {
                return BuildSelectClauseForType(targetType, depth);
            }
            var leftPadding = new string(' ', depth * 2);

            var dotIndex = include.IndexOf(".", StringComparison.InvariantCultureIgnoreCase);

            var currentIncludeName = dotIndex >= 0 ? include.Substring(0, dotIndex) : include;

            Type propertyType;
            var propertyInfo = targetType.GetProperty(currentIncludeName, BindingFlags.IgnoreCase | BindingFlags.Public | BindingFlags.Instance);

            var includeName = currentIncludeName.ToCamelCase();

            var includeMethodInfo = includeDetails.MethodIncludes.Count > parameterIndex ? includeDetails.MethodIncludes[parameterIndex].Method : null;
            var includeByMethod = includeMethodInfo != null && currentIncludeName == includeMethodInfo.Name && propertyInfo.PropertyType == includeMethodInfo.ReturnType;

            if (includeByMethod)
            {
                var methodDetails = includeDetails.MethodIncludes[parameterIndex];
                parameterIndex++;

                propertyType = methodDetails.Method.ReturnType.GetTypeOrListType();

                var includeMethodParams = methodDetails.Parameters.Where(pair => pair.Value != null).ToList();
                includeName = methodDetails.Method.Name.ToCamelCase();

                if (includeMethodParams.Any())
                {
                    var includeParameters = string.Join(", ", includeMethodParams.Select(pair => pair.Key + ": $" + pair.Key + parameterPrefix + parameterIndex));
                    includeName = $"{includeName}({includeParameters})";

                    foreach (var item in includeMethodParams)
                    {
                        includeVariables.Add(item.Key + parameterPrefix + parameterIndex, item.Value);
                    }
                }
            }
            else
            {
                propertyType = propertyInfo.PropertyType.GetTypeOrListType();
            }

            if (propertyType.IsValueTypeOrString())
            {
                return leftPadding + includeName;
            }

            var restOfTheInclude = new IncludeDetails(includeDetails.MethodIncludes) { Path = dotIndex >= 0 ? include.Substring(dotIndex + 1) : "" };

            var fieldsFromInclude = BuildSelectClauseForInclude(propertyType, restOfTheInclude, includeVariables, parameterPrefix, parameterIndex, depth + 1);
            fieldsFromInclude = $"{leftPadding}{includeName} {{{Environment.NewLine}{fieldsFromInclude}{Environment.NewLine}{leftPadding}}}";
            return fieldsFromInclude;
        }
    }

    class GraphQLQuery
    {
        public GraphQLQuery(string query, IReadOnlyDictionary<string, object> variables, string fullQuery)
        {
            Query = query;
            Variables = variables;
            FullQuery = fullQuery;
        }

        public string Query { get; }
        public string FullQuery { get; }
        public IReadOnlyDictionary<string, object> Variables { get; }
    }

    class SelectClauseDetails
    {
        public string SelectClause { get; set; }
        public Dictionary<string, object> IncludeArguments { get; set; }
    }
}