using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using GraphQLinq.Shared.Scaffolding;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Formatting;
using Spectre.Console;
using static Microsoft.CodeAnalysis.CSharp.SyntaxFactory;

namespace GraphQLinq.Scaffolding
{
    class GraphQLClassesGenerator
    {
        List<string> usings = new() { "System", "System.Collections.Generic", "GraphQLinq" };
        List<string> usingsQueryContext = new() { "System", "System.Collections.Generic", "GraphQLinq", "UnityEngine.Networking" };

        private Dictionary<string, string> renamedClasses = new();
        private readonly CodeGenerationOptions options;

        private static readonly Dictionary<string, (string Name, Type type)> TypeMapping = new(StringComparer.InvariantCultureIgnoreCase)
        {
            { "Int", new("int", typeof(int)) },
            { "Float", new("float", typeof(float)) },
            { "String", new("string", typeof(string)) },
            { "ID", new("ID", typeof(ID)) },
            { "Date", new("DateTime", typeof(DateTime)) },
            { "Boolean", new("bool", typeof(bool)) },
            { "Long", new("long", typeof(long)) },
            { "uuid", new("Guid", typeof(Guid)) },
            { "timestamptz", new("DateTimeOffset", typeof(DateTimeOffset)) },
            { "Uri", new("Uri", typeof(Uri)) }
        };

        private static readonly List<string> BuiltInTypes = new()
        {
            "ID",
            "Int",
            "Float",
            "String",
            "Boolean"
        };

        private static readonly AdhocWorkspace Workspace = new();

        public GraphQLClassesGenerator(CodeGenerationOptions options)
        {
            this.options = options;
        }

        public string GenerateClient(Schema schema, string endpointUrl)
        {
            var queryType = schema.QueryType.Name;
            var mutationType = schema.MutationType?.Name;
            var subscriptionType = schema.SubscriptionType?.Name;

            var types = schema.Types.Where(type => !type.Name.StartsWith("__")
                                                                && !BuiltInTypes.Contains(type.Name)
                                                                && queryType != type.Name && mutationType != type.Name && subscriptionType != type.Name).ToList();

            var enums = types.Where(type => type.Kind == TypeKind.Enum);
            var classes = types.Where(type => type.Kind == TypeKind.Object || type.Kind == TypeKind.InputObject || type.Kind == TypeKind.Union).OrderBy(type => type.Name);
            var interfaces = types.Where(type => type.Kind == TypeKind.Interface);

            AnsiConsole.WriteLine("Scaffolding enums ...");
            foreach (var enumInfo in enums)
            {
                var syntax = GenerateEnum(enumInfo);
                var name = GetEnumName(enumInfo.Name);
                FormatAndWriteToFile(syntax, name);
            }

            AnsiConsole.WriteLine("Scaffolding classes ...");
            foreach (var classInfo in classes)
            {
                var syntax = GenerateClass(classInfo);
                var suffix = TypeKindNeedSuffix(classInfo.Kind) ? options.Suffix : "";
                FormatAndWriteToFile(syntax, classInfo.Name + suffix);
            }

            AnsiConsole.WriteLine("Scaffolding interfaces ...");
            foreach (var interfaceInfo in interfaces)
            {
                if (options.UseEntity && interfaceInfo.Name == nameof(CustomEntity))
                    continue;

                var syntax = GenerateInterface(interfaceInfo);
                var name = GetInterfaceName(interfaceInfo.Name);
                FormatAndWriteToFile(syntax, name);
            }

            var classesWithArgFields = classes.Where(type => (type.Fields ?? new List<Field>()).Any(field => field.Args.Any())).ToList();

            AnsiConsole.WriteLine("Scaffolding Query Extensions ...");
            var queryExtensions = GenerateQueryExtensions(classesWithArgFields);
            FormatAndWriteToFile(queryExtensions, "QueryExtensions");

            // Generate Queries into QueryContext
            var queryContextName = $"{options.ContextName}{queryType}Context";
            var queryClass = schema.Types.SingleOrDefault(type => type.Name == queryType);
            if (queryClass != null)
            {
                AnsiConsole.WriteLine($"Scaffolding {queryContextName} ...");
                var graphContext = GenerateGraphContext(queryClass, endpointUrl);
                FormatAndWriteToFile(graphContext, queryContextName);
            }

            // Generate Mutations into MutationContext
            var mutationContextName = $"{options.ContextName}{mutationType}Context";
            var mutationClass = schema.Types.SingleOrDefault(type => type.Name == mutationType);
            if (mutationClass != null)
            {
                AnsiConsole.WriteLine($"Scaffolding {mutationContextName}...");
                var mutationContext = GenerateGraphContext(mutationClass, endpointUrl);
                FormatAndWriteToFile(mutationContext, mutationContextName);
            }

            return $"{options.ContextName}Context";
        }


        private SyntaxNode GenerateEnum(GraphqlType enumInfo)
        {
            var topLevelDeclaration = RoslynUtilities.GetTopLevelNode(options.Namespace);
            var name = GetEnumName(enumInfo.Name).NormalizeIfNeeded(options);

            var declaration = EnumDeclaration(name).AddModifiers(Token(SyntaxKind.PublicKeyword));

            // Add enum comments
            var doc = CreateTriviaComment(enumInfo.Description);
            if (doc != null)
                declaration = declaration.WithLeadingTrivia(doc);

            foreach (var enumValue in enumInfo.EnumValues)
            {
                declaration = declaration.AddMembers(EnumMemberDeclaration(Identifier(EscapeIdentifierName(enumValue.Name))));
            }

            declaration = declaration.AddMembers(EnumMemberDeclaration(Identifier(EscapeIdentifierName("COUNT"))));

            return topLevelDeclaration.AddMembers(declaration);
        }

        private SyntaxNode GenerateClass(GraphqlType classInfo)
        {
            var topLevelDeclaration = RoslynUtilities.GetTopLevelNode(options.Namespace);

            var semicolonToken = Token(SyntaxKind.SemicolonToken);

            var className = (classInfo.Name + options.Suffix).NormalizeIfNeeded(options);

            var declaration = ClassDeclaration(className)
                                .AddModifiers(Token(SyntaxKind.PublicKeyword), Token(SyntaxKind.PartialKeyword));

            // Add class comments
            var doc = CreateTriviaComment(classInfo.Description);
            if (doc != null)
                declaration = declaration.WithLeadingTrivia(doc);

            var isEntity = classInfo.Interfaces?.Any(x =>
            {
                string name = GetInterfaceName(x.Name);
                return name == nameof(CustomEntity.IEntity);
            }) ?? false;

            if (options.UseEntity && isEntity)
                declaration = declaration.AddBaseListTypes(SimpleBaseType(ParseTypeName(nameof(Entity))));

            foreach (var @interface in classInfo.Interfaces ?? new List<GraphqlType>())
            {
                string name = GetInterfaceName(@interface.Name);
                if (options.UseEntity && name == nameof(CustomEntity.IEntity))
                    continue;

                declaration = declaration.AddBaseListTypes(SimpleBaseType(ParseTypeName(name)));
            }

            foreach (var field in classInfo.Fields ?? classInfo.InputFields ?? new List<Field>())
            {
                var fieldName = field.Name.NormalizeIfNeeded(options);

                // Ignore ID field if it's an entity
                if (options.UseEntity && isEntity && fieldName == nameof(CustomEntity.IEntity.Id))
                    continue;

                if (fieldName == className)
                {
                    declaration = declaration.ReplaceToken(declaration.Identifier, Identifier($"{className}Type"));
                    renamedClasses.Add(className, $"{className}Type");
                }

                var (fieldTypeName, fieldType) = GetSharpTypeName(field.Type);

                if (NeedsNullable(fieldType, field.Type, true))
                {
                    fieldTypeName += "?";
                }

                var property = PropertyDeclaration(ParseTypeName(fieldTypeName), fieldName)
                                            .AddModifiers(Token(SyntaxKind.PublicKeyword));

                // Add field comments
                doc = CreateTriviaComment(field.Description);
                if (doc != null)
                    property = property.WithLeadingTrivia(doc);

                property = property.AddAccessorListAccessors(AccessorDeclaration(SyntaxKind.GetAccessorDeclaration)
                                   .WithSemicolonToken(semicolonToken));

                property = property.AddAccessorListAccessors(AccessorDeclaration(SyntaxKind.SetAccessorDeclaration)
                                   .WithSemicolonToken(semicolonToken));

                declaration = declaration.AddMembers(property);
            }

            foreach (var @using in usings)
            {
                topLevelDeclaration = topLevelDeclaration.AddUsings(UsingDirective(IdentifierName(@using)));
            }

            topLevelDeclaration = topLevelDeclaration.AddMembers(declaration);

            return topLevelDeclaration;
        }

        private string GetInterfaceName(string name)
        {
            if (options.UseInterfaceAndEnumPrefix)
                return "I" + name;

            return name;
        }
        private string GetEnumName(string name)
        {
            if (options.UseInterfaceAndEnumPrefix)
                return "E" + name;

            return name;
        }

        private static SyntaxTriviaList? CreateTriviaComment(string description)
        {
            if (string.IsNullOrEmpty(description))
                return null;

            // replace string line break by xml line break
            var tokens = description.Split('\n')
                            .Select(line => XmlTextLiteral(line))
                            .ToList();
            for (int i = 1; i < tokens.Count; i += 2)
                tokens.Insert(i, XmlTextNewLine(Environment.NewLine));

            // add new line between summmary tags
            tokens.Insert(0, XmlTextNewLine(Environment.NewLine));
            tokens.Add(XmlTextNewLine(Environment.NewLine));

            // Create comment
            var summary = XmlElement("summary", SingletonList<XmlNodeSyntax>(XmlText(TokenList(tokens))));
            SyntaxTriviaList doc = TriviaList(Trivia(DocumentationComment(summary, XmlText(Environment.NewLine))));
            return doc;
        }

        private SyntaxNode GenerateInterface(GraphqlType interfaceInfo)
        {
            var topLevelDeclaration = RoslynUtilities.GetTopLevelNode(options.Namespace);

            var semicolonToken = Token(SyntaxKind.SemicolonToken);

            var name = GetInterfaceName(interfaceInfo.Name).NormalizeIfNeeded(options);

            var declaration = InterfaceDeclaration(name).AddModifiers(Token(SyntaxKind.PublicKeyword), Token(SyntaxKind.PartialKeyword));

            // Add interface comments
            var doc = CreateTriviaComment(interfaceInfo.Description);
            if (doc != null)
                declaration = declaration.WithLeadingTrivia(doc);

            foreach (var field in interfaceInfo.Fields)
            {
                var (fieldTypeName, fieldType) = GetSharpTypeName(field.Type, true);

                if (NeedsNullable(fieldType, field.Type))
                {
                    fieldTypeName += "?";
                }

                var fieldName = field.Name.NormalizeIfNeeded(options);

                var property = PropertyDeclaration(ParseTypeName(fieldTypeName), fieldName);

                // Add field comments
                doc = CreateTriviaComment(field.Description);
                if (doc != null)
                    property = property.WithLeadingTrivia(doc);

                property = property.AddAccessorListAccessors(AccessorDeclaration(SyntaxKind.GetAccessorDeclaration)
                                   .WithSemicolonToken(semicolonToken));

                property = property.AddAccessorListAccessors(AccessorDeclaration(SyntaxKind.SetAccessorDeclaration)
                                   .WithSemicolonToken(semicolonToken));

                declaration = declaration.AddMembers(property);
            }

            foreach (var @using in usings)
            {
                topLevelDeclaration = topLevelDeclaration.AddUsings(UsingDirective(IdentifierName(@using)));
            }

            return topLevelDeclaration.AddMembers(declaration);
        }

        private SyntaxNode GenerateQueryExtensions(List<GraphqlType> classesWithArgFields)
        {
            var exceptionMessage = Literal("This method is not implemented. It exists solely for query purposes.");
            var argumentListSyntax = ArgumentList(SingletonSeparatedList(Argument(LiteralExpression(SyntaxKind.StringLiteralExpression, exceptionMessage))));

            var notImplemented = ThrowStatement(ObjectCreationExpression(IdentifierName("NotImplementedException"), argumentListSyntax, null));

            var topLevelDeclaration = RoslynUtilities.GetTopLevelNode(options.Namespace);

            var declaration = ClassDeclaration("QueryExtensions")
                                            .AddModifiers(Token(SyntaxKind.PublicKeyword), Token(SyntaxKind.StaticKeyword));

            foreach (var @class in classesWithArgFields)
            {
                foreach (var field in @class.Fields.Where(f => f.Args.Any()))
                {
                    var (fieldTypeName, _) = GetSharpTypeName(field.Type);

                    var fieldName = field.Name.NormalizeIfNeeded(options);

                    var methodDeclaration = MethodDeclaration(ParseTypeName(fieldTypeName), fieldName)
                                            .AddModifiers(Token(SyntaxKind.PublicKeyword), Token(SyntaxKind.StaticKeyword));

                    var identifierName = EscapeIdentifierName(@class.Name.ToCamelCase());

                    var thisParameter = Parameter(Identifier(identifierName))
                                             .WithType(ParseTypeName(@class.Name.NormalizeIfNeeded(options)))
                                             .WithModifiers(TokenList(Token(SyntaxKind.ThisKeyword)));

                    var methodParameters = new List<ParameterSyntax> { thisParameter };

                    foreach (var arg in field.Args)
                    {
                        (fieldTypeName, _) = GetSharpTypeName(arg.Type);

                        var typeName = TypeMapping.Values.Any(tuple => tuple.Name == fieldTypeName) ? fieldTypeName : fieldTypeName.NormalizeIfNeeded(options);

                        var parameterSyntax = Parameter(Identifier(arg.Name)).WithType(ParseTypeName(typeName));
                        methodParameters.Add(parameterSyntax);
                    }

                    methodDeclaration = methodDeclaration.AddParameterListParameters(methodParameters.ToArray())
                                             .WithBody(Block(notImplemented));

                    declaration = declaration.AddMembers(methodDeclaration);
                }
            }

            foreach (var @using in usings)
            {
                topLevelDeclaration = topLevelDeclaration.AddUsings(UsingDirective(IdentifierName(@using)));
            }

            topLevelDeclaration = topLevelDeclaration.AddMembers(declaration);

            return topLevelDeclaration;
        }

        private SyntaxNode GenerateGraphContext(GraphqlType queryInfo, string endpointUrl)
        {
            var topLevelDeclaration = RoslynUtilities.GetTopLevelNode(options.Namespace);

            var className = $"{options.ContextName}{queryInfo.Name}Context";
            var declaration = ClassDeclaration(className)
                .AddModifiers(Token(SyntaxKind.PublicKeyword), Token(SyntaxKind.PartialKeyword))
                .AddBaseListTypes(SimpleBaseType(ParseTypeName("GraphContext")));

            // [MM] XK - generate a prop to store the json of args for all queries (to handle non-nullable types)
            declaration = AddQueriesArgsJsonProp(queryInfo, declaration);

            var thisInitializer = ConstructorInitializer(SyntaxKind.ThisConstructorInitializer)
                                    .AddArgumentListArguments(Argument(LiteralExpression(SyntaxKind.StringLiteralExpression, Literal(endpointUrl))));

            var defaultConstructorDeclaration = ConstructorDeclaration(className)
                .AddModifiers(Token(SyntaxKind.PublicKeyword))
                .WithInitializer(thisInitializer)
                .WithBody(Block());

            var baseInitializer = ConstructorInitializer(SyntaxKind.BaseConstructorInitializer)
                                    .AddArgumentListArguments(Argument(IdentifierName("baseUrl")));

            var baseUrlConstructorDeclaration = ConstructorDeclaration(className)
                                    .AddModifiers(Token(SyntaxKind.PublicKeyword))
                                    .AddParameterListParameters(Parameter(Identifier("baseUrl")).WithType(ParseTypeName("string")))
                                    .WithInitializer(baseInitializer)
                                    .WithBody(Block());


            var baseInitializerWithHeaders = ConstructorInitializer(SyntaxKind.BaseConstructorInitializer)
                                    .AddArgumentListArguments(Argument(IdentifierName("baseUrl")), Argument(IdentifierName("headers")));

            var baseUrlWithHeadersConstructorDeclaration = ConstructorDeclaration(className)
                                    .AddModifiers(Token(SyntaxKind.PublicKeyword))
                                    .AddParameterListParameters(Parameter(Identifier("baseUrl")).WithType(ParseTypeName("string")))
                                    .AddParameterListParameters(Parameter(Identifier("headers")).WithType(ParseTypeName("Dictionary<string, string>")))
                                    .WithInitializer(baseInitializerWithHeaders)
                                    .WithBody(Block());

            declaration = declaration.AddMembers(defaultConstructorDeclaration, baseUrlConstructorDeclaration, baseUrlWithHeadersConstructorDeclaration);

            foreach (var field in queryInfo.Fields)
            {
                if (field.Type.Name == queryInfo.Name || field.Type.OfType?.Name == queryInfo.Name)
                {
                    continue; //Workaround for Query.relay method in GitHub schema
                }

                var (fieldTypeName, fieldType) = GetSharpTypeName(field.Type.Kind == TypeKind.NonNull ? field.Type.OfType : field.Type, true);

                var baseMethodName = fieldTypeName.Replace("GraphItemQuery", "BuildItemQuery")
                                         .Replace("GraphCollectionQuery", "BuildCollectionQuery");

                var fieldName = field.Name.NormalizeIfNeeded(options);

                var methodDeclaration = MethodDeclaration(ParseTypeName(fieldTypeName), fieldName)
                                            .AddModifiers(Token(SyntaxKind.PublicKeyword));

                // Add method comments
                var doc = CreateTriviaComment(field.Description);
                if (doc != null)
                    methodDeclaration = methodDeclaration.WithLeadingTrivia(doc);

                var methodParameters = new List<ParameterSyntax>();

                var initializer = InitializerExpression(SyntaxKind.ArrayInitializerExpression);

                foreach (var arg in field.Args)
                {
                    (fieldTypeName, fieldType) = GetSharpTypeName(arg.Type);

                    if (NeedsNullable(fieldType, arg.Type) && arg.Type.Kind != TypeKind.NonNull)
                    {
                        fieldTypeName += "?";
                    }

                    var parameterSyntax = Parameter(Identifier(arg.Name)).WithType(ParseTypeName(fieldTypeName));
                    methodParameters.Add(parameterSyntax);

                    initializer = initializer.AddExpressions(IdentifierName(arg.Name));
                }

                var paramsArray = ArrayCreationExpression(ArrayType(ParseTypeName("object[]")), initializer);

                var parametersDeclaration = LocalDeclarationStatement(VariableDeclaration(IdentifierName("var"))
                                            .WithVariables(SingletonSeparatedList(VariableDeclarator(Identifier("parameterValues"))
                                            .WithInitializer(EqualsValueClause(paramsArray)))));

                var parametersArgument = Argument(IdentifierName("parameterValues"));
                var argumentSyntax = Argument(LiteralExpression(SyntaxKind.StringLiteralExpression, Literal($"{field.Name}")));

                var returnStatement = ReturnStatement(InvocationExpression(IdentifierName(baseMethodName))
                                            .WithArgumentList(ArgumentList(SeparatedList(new List<ArgumentSyntax> { parametersArgument, argumentSyntax }))));

                methodDeclaration = methodDeclaration.AddParameterListParameters(methodParameters.ToArray())
                                                        .WithBody(Block(parametersDeclaration, returnStatement));

                declaration = declaration.AddMembers(methodDeclaration);
            }

            foreach (var @using in usingsQueryContext)
            {
                topLevelDeclaration = topLevelDeclaration.AddUsings(UsingDirective(IdentifierName(@using)));
            }

            topLevelDeclaration = topLevelDeclaration.AddMembers(declaration);

            return topLevelDeclaration;
        }

        /// <summary>
        /// [MM] XK - this method will write in the class a property set with a json string that containt the args type of all queries
        /// this take into account the non-nullable types
        /// </summary>
        /// <param name="queryInfo"></param>
        /// <param name="declaration"></param>
        /// <returns></returns>
        private ClassDeclarationSyntax AddQueriesArgsJsonProp(GraphqlType queryInfo, ClassDeclarationSyntax declaration)
        {
            var json = GenerateQueryArgsJson(queryInfo);

            declaration = declaration.WithMembers(
                    SingletonList<MemberDeclarationSyntax>(
                    PropertyDeclaration(PredefinedType(Token(SyntaxKind.StringKeyword)), Identifier("QueriesArgsJson"))
                    .WithModifiers(TokenList(Token(SyntaxKind.ProtectedKeyword), Token(SyntaxKind.OverrideKeyword)))
                    .WithAccessorList(
                        AccessorList(
                            List<AccessorDeclarationSyntax>(
                                new AccessorDeclarationSyntax[]{
                                    AccessorDeclaration(SyntaxKind.GetAccessorDeclaration)
                                        .WithSemicolonToken(Token(SyntaxKind.SemicolonToken))})))
                    .WithInitializer(EqualsValueClause(
                        LiteralExpression(SyntaxKind.StringLiteralExpression, Literal(json))))
                    .WithSemicolonToken(Token(SyntaxKind.SemicolonToken))));
            return declaration;
        }

        /// <summary>
        /// [MM] XK - serialise in json the args types of all queries
        /// </summary>
        /// <param name="queryInfo"></param>
        /// <returns></returns>
        private string GenerateQueryArgsJson(GraphqlType queryInfo)
        {
            var queriesArgs = new QueriesArgs();

            foreach (var query in queryInfo.Fields)
            {
                var queryArgs = new QueryArgs();
                foreach (var arg in query.Args)
                {
                    var typeStr = GetTypeFromFieldType(arg.Type);
                    queryArgs.Add(arg.Name, typeStr);
                }
                queriesArgs.Add(query.Name, queryArgs);
            }

            return JsonSerializer.Serialize(queriesArgs);
        }

        /// <summary>
        /// [MM] XK - wrote the GraphQL type and take into account the non-nullable types
        /// </summary>
        /// <param name="field"></param>
        /// <returns></returns>
        private string GetTypeFromFieldType(FieldType field)
        {
            if (field.OfType == null)
                return field.Name ?? "";

            var strFormat = "";
            switch (field.Kind)
            {
                case TypeKind.List:
                    strFormat = "[{0}]";
                    break;
                case TypeKind.NonNull:
                    strFormat = "{0}!";
                    break;
                case TypeKind.InputObject:
                case TypeKind.Object:
                    strFormat = "{0}" + options.Suffix;
                    break;
                case TypeKind.Scalar:
                case TypeKind.Interface:
                case TypeKind.Union:
                case TypeKind.Enum:
                default:
                    strFormat = "{0}";
                    break;
            }

            var subType = GetTypeFromFieldType(field.OfType);
            return string.Format(strFormat, subType);
        }

        private static bool NeedsNullable(Type? systemType, FieldType type, bool forceNullable = false)
        {
            if (systemType == typeof(ID))
                forceNullable = false;

            if (systemType == null)
            {
                return false;
            }

            return (type.Kind == TypeKind.Scalar || forceNullable) && (systemType.IsValueType || systemType == typeof(Enum));
        }


        private void FormatAndWriteToFile(SyntaxNode syntax, string name)
        {
            if (!Directory.Exists(options.OutputDirectory))
            {
                Directory.CreateDirectory(options.OutputDirectory);
            }

            name = name.NormalizeIfNeeded(options);

            var fileName = Path.Combine(options.OutputDirectory, name + ".cs");
            using (var streamWriter = File.CreateText(fileName))
            {
                Formatter.Format(syntax, Workspace).WriteTo(streamWriter);
            }
        }

        private (string typeName, Type? typeType) GetSharpTypeName(FieldType? fieldType, bool wrapWithGraphTypes = false)
        {
            if (fieldType == null)
            {
                throw new NotImplementedException("ofType nested more than three levels not implemented");
            }

            var typeName = fieldType.Name;
            Type? resultType;

            if (typeName == null)
            {
                switch (fieldType.Kind)
                {
                    case TypeKind.List:
                        {
                            var type = GetSharpTypeName(fieldType.OfType).typeName;
                            typeName = wrapWithGraphTypes ? $"GraphCollectionQuery<{type}>" : $"List<{type}>";
                            return (typeName, null);
                        }
                    default:
                        return GetSharpTypeName(fieldType.OfType);
                }
            }
            else
            {
                (typeName, resultType) = GetMappedType(fieldType.Name, fieldType.Kind);

                if (resultType == null && fieldType.Kind == TypeKind.Scalar)
                {
                    (typeName, resultType) = GetMappedType("string", TypeKind.Scalar);
                }
            }

            if (wrapWithGraphTypes)
            {
                typeName = $"GraphItemQuery<{typeName}>";
                resultType = null;
            }

            if (renamedClasses.ContainsKey(typeName))
            {
                typeName = renamedClasses[typeName];
                resultType = null;
            }

            return (typeName, resultType);
        }

        private (string, Type?) GetMappedType(string name, TypeKind type)
        {
            if (type == TypeKind.Enum)
            {
                return (GetEnumName(name).NormalizeIfNeeded(options), typeof(Enum));
            }

            return TypeMapping.ContainsKey(name) 
                ? TypeMapping[name] 
                : new (AddSuffixIfNeeded(name, type).NormalizeIfNeeded(options), null);
        }

        private string AddSuffixIfNeeded(string name, TypeKind type)
        {
            if (TypeKindNeedSuffix(type))
                return name + options.Suffix;

            return name;
        }

        private bool TypeKindNeedSuffix(TypeKind type)
        {
            return type == TypeKind.Object || type == TypeKind.InputObject;
        }

        private string EscapeIdentifierName(string name)
        {
            return SyntaxFacts.GetKeywordKind(name) != SyntaxKind.None ? $"@{name}" : name;
        }
    }
}