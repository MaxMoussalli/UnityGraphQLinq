using System;

namespace GraphQLinq
{
    public class UnityWebRequestException : Exception
    {
        public UnityWebRequestException(string error, string query)
            : base($"{error}\n -> query: {query}")
        {
            GraphQLQuery = query;
            Error = error;
        }

        public string GraphQLQuery { get; private set; }
        public string Error { get; private set; }
    }
}