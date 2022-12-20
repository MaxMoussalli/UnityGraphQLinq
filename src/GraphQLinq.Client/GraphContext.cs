using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;
using GraphQLinq.Shared.Scaffolding;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;
using UnityEngine.Networking;

namespace GraphQLinq
{
    public abstract class GraphContext : IDisposable
    {
        private readonly bool ownsHttpClient = false;

        // [MM] XK - Replace HttpClient by UnityWebRequest to make plugin compatible with Unity
        public UnityWebRequest webRequest { get; }

        // [MM] XK - Add json query args schema to handle non-nullables
        protected abstract string QueriesArgsJson { get; }
        private QueriesArgs m_QueriesArgs;

        protected GraphContext(UnityWebRequest webRequest)
        {
            if (webRequest == null)
            {
                throw new ArgumentNullException($"{nameof(webRequest)} cannot be null");
            }

            if (webRequest.url == null)
            {
                throw new ArgumentException($"{nameof(webRequest.url)} cannot be empty");
            }

            this.webRequest = webRequest;

            InitQueriesArgs();
        }

        protected GraphContext(string baseUrl, string authorization)
        {
            if (string.IsNullOrEmpty(baseUrl))
            {
                throw new ArgumentException($"{nameof(baseUrl)} cannot be empty");
            }

            ownsHttpClient = true;
            webRequest = UnityWebRequest.Post(baseUrl, UnityWebRequest.kHttpVerbPOST);
            webRequest.SetRequestHeader("Content-Type", "application/json");

            if (!string.IsNullOrEmpty(authorization))
            {
                webRequest.SetRequestHeader("Authorization", "Bearer " + authorization);
            }

            InitQueriesArgs();
        }

        public JsonSerializerSettings JsonSerializerOptions { get; set; } = CreateJsonSerializerSettings();

        // [MM] XK - Replace System.Text.Json by Newtonsoft.Json to make plugin compatible with Unity
        public static JsonSerializerSettings CreateJsonSerializerSettings()
        {
            var contractResolver = new DefaultContractResolver
            {
                NamingStrategy = new CamelCaseNamingStrategy()
            };

            return new JsonSerializerSettings
            {
                ContractResolver = contractResolver,
                Converters = { new StringEnumConverter() },
                DefaultValueHandling = DefaultValueHandling.Ignore,
            };
        }

        protected GraphCollectionQuery<T> BuildCollectionQuery<T>(object[] parameterValues, [CallerMemberName] string queryName = null)
        {
            var arguments = BuildArgumentDictionnary(parameterValues, queryName);
            return new GraphCollectionQuery<T, T>(this, queryName) { Arguments = arguments };
        }

        protected GraphItemQuery<T> BuildItemQuery<T>(object[] parameterValues, [CallerMemberName] string queryName = null)
        {
            var arguments = BuildArgumentDictionnary(parameterValues, queryName);
            return new GraphItemQuery<T, T>(this, queryName) { Arguments = arguments };
        }

        private Dictionary<string, object> BuildArgumentDictionnary(object[] parameterValues, string queryName)
        {
            var parameters = GetType().GetMethod(queryName, BindingFlags.Public | BindingFlags.IgnoreCase | BindingFlags.Instance).GetParameters();
            var arguments = parameters.Zip(parameterValues, (info, value) =>
            new
            {
                Name = info.Name,
                Value = value,
            })
            .ToDictionary(arg => arg.Name, arg => arg.Value);

            return arguments;
        }

        public async Task<string> PostAsync(string query)
        {
            Console.WriteLine("query: " + query);

            byte[] postData = Encoding.ASCII.GetBytes(query);
            webRequest.uploadHandler = new UploadHandlerRaw(postData);

            webRequest.SendWebRequest();
            while (!webRequest.isDone)
                await Task.Yield(); // to keep in sync with unity thread

            if (!string.IsNullOrEmpty(webRequest.error))
                throw new UnityWebRequestException(webRequest.error, query);

            return webRequest.downloadHandler.text;
        }

        public void Dispose()
        {
            if (ownsHttpClient)
            {
                webRequest.Dispose();
            }
        }

        private void InitQueriesArgs()
        {
            m_QueriesArgs = JsonConvert.DeserializeObject<QueriesArgs>(QueriesArgsJson);
        }

        public string GetArgsDefinition(string queryName, string argName)
        {
            m_QueriesArgs.TryGetValue(queryName, out var args);
            if (args == null)
                return "";

            args.TryGetValue(argName, out var value);
            if (value == null)
                return "";

            return value;
        }
    }
}