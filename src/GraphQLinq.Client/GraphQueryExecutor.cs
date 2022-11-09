using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace GraphQLinq
{
    class GraphQueryExecutor<T, TSource>
    {
        private readonly GraphContext context;
        private readonly string query;
        private readonly QueryType queryType;
        private readonly Func<TSource, T> mapper;

        // [MM] XK - Replace System.Text.Json by Newtonsoft.Json to make plugin compatible with Unity
        private readonly JsonSerializerSettings jsonSerializerSettings;
        private readonly JsonSerializer serializer;

        private const string DataPathPropertyName = "data";
        private const string ErrorPathPropertyName = "errors";

        internal GraphQueryExecutor(GraphContext context, string query, QueryType queryType, Func<TSource, T> mapper)
        {
            this.context = context;
            this.query = query;
            this.mapper = mapper;
            this.queryType = queryType;

            // [MM] XK - Replace System.Text.Json by Newtonsoft.Json to make plugin compatible with Unity
            jsonSerializerSettings = context.JsonSerializerOptions;
            serializer = JsonSerializer.Create(jsonSerializerSettings);
        }

        // [MM] XK - Replace System.Text.Json by Newtonsoft.Json to make plugin compatible with Unity
        private T JsonElementToItem(JToken jsonElement)
        {
            if (mapper != null)
            {
                var result = jsonElement.ToObject<TSource>(serializer);
                return mapper.Invoke(result);
            }
            else
            {
                var result = jsonElement.ToObject<T>(serializer);
                return result;
            }
        }

        // [MM] XK - Replace System.Text.Json by Newtonsoft.Json to make plugin compatible with Unity
        internal async Task<(T Item, IEnumerable<T> Enumerable)> Execute()
        {
            Console.WriteLine("query: " + query);

            using (var content = new StringContent(query, Encoding.UTF8, "application/json"))
            {
                using (var response = await context.HttpClient.PostAsync("", content))
                {
                    using (var stream = await response.Content.ReadAsStreamAsync())
                    {
                        using (var streamReader = new StreamReader(stream))
                        {
                            var streamContent = await streamReader.ReadToEndAsync();
                            var doc = JObject.Parse(streamContent);

                            var error = doc.Root.SelectToken(ErrorPathPropertyName);
                            if (error != null)
                            {
                                var errors = error.ToObject<List<GraphQueryError>>(serializer);
                                throw new GraphQueryExecutionException(errors, query);
                            }

                            var dataElement = doc.Root.SelectToken(DataPathPropertyName);
                            if (dataElement == null)
                            {
                                throw new GraphQueryExecutionException(query);
                            }

                            var resultElement = dataElement.SelectToken(GraphQueryBuilder<T>.ResultAlias);
                            if (resultElement == null)
                            {
                                throw new GraphQueryExecutionException(query);
                            }

                            if (queryType == QueryType.Item)
                            {
                                return (JsonElementToItem(resultElement), null);
                            }

                            return (default, resultElement.Children().Select(JsonElementToItem));
                        }
                    }
                }
            }
        }
    }
}