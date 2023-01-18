using System;
using System.Collections.Generic;
using System.Text;

namespace GraphQLinq
{
    public class GraphQueryExecutionException : Exception
    {

        public string GraphQLQuery { get; private set; }
        public string GraphQLQueryResponse { get; private set; }
        public IEnumerable<GraphQueryError> Errors { get; private set; }

        public GraphQueryExecutionException(string query, string queryResponse) 
            : base("Unexpected error response received from server.")
        {
            GraphQLQuery = query;
            GraphQLQueryResponse = queryResponse;
        }

        public GraphQueryExecutionException(IEnumerable<GraphQueryError> errors, string query)
            : base($"One or more errors occurred during query execution. Check bellow for details")
        {
            Errors = errors;
            GraphQLQuery = query;
        }

        public override string Message => base.Message + "\n" + ToString();

        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();

            const int separatorCharCount = 50;

            sb.Append('\n');
            sb.Append('=', separatorCharCount);
            sb.Append('\n');

            if (Errors != null)
            {
                foreach (var err in Errors)
                {
                    sb.Append('-', separatorCharCount);
                    sb.Append('\n');
                    sb.Append(err.ToString());
                    sb.Append('\n');
                    sb.Append('-', separatorCharCount);
                    sb.Append('\n');
                }

                sb.Append('\n');
            }

            if (!string.IsNullOrEmpty(GraphQLQuery))
            {
                sb.Append("GraphQL Query: \n");
                sb.Append(GraphQLQuery);
            }

            if (!string.IsNullOrEmpty(GraphQLQueryResponse))
            {
                sb.Append("GraphQL Query Response: \n");
                sb.Append(GraphQLQueryResponse);
            }

            sb.Append('\n');
            sb.Append('=', separatorCharCount);
            sb.Append('\n');

            return sb.ToString();
        }
    }
    
    public class GraphQueryError
    {
        public string Message { get; set; }
        public ErrorLocation[] Locations { get; set; }
        public ErrorExtensions Extensions { get; set; }
        public string[] Path { get; set; }

        public override string ToString()
        {
            StringBuilder sb = new StringBuilder();

            sb.Append(Message);
            sb.Append('\n');

            sb.Append($"Path:");
            sb.Append('\n');
            foreach (var subPath in Path)
            {
                sb.Append('\t');
                sb.Append(subPath);
                sb.Append('\n');
            }

            sb.Append($"Error Code: {Extensions.Code}");
            sb.Append('\n');

            sb.Append($"Server Stacktrace:");
            sb.Append('\n');
            foreach (var stack in Extensions.Exception.Stacktrace)
            {
                sb.Append('\t');
                sb.Append(stack);
                sb.Append('\n');
            }

            return sb.ToString();
        }
    }

    public class ErrorLocation
    {
        public int Line { get; set; }
        public int Column { get; set; }
    }

    public class ErrorExtensions
    {
        public string Code { get; set; }
        public ErrorExceptionStacktrace Exception { get; set; }
    }

    public class ErrorExceptionStacktrace
    {
        public string[] Stacktrace { get; set; }
    }
}