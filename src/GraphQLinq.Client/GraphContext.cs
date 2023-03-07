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
        public delegate void OnLogHandler(string log);
        public OnLogHandler OnLog;

        private string m_BaseURL;

        // [MM] XK - Add json query args schema to handle non-nullables
        protected abstract string QueriesArgsJson { get; }
        private QueriesArgs m_QueriesArgs;

        public Dictionary<string, string> Headers { get; set; }

        public JsonSerializerSettings JsonSerializerOptions { get; set; } = CreateJsonSerializerSettings();

        public abstract string QueryKeyword { get; }

        protected GraphContext(string baseUrl, Dictionary<string, string> headers = null)
        {
            if (string.IsNullOrEmpty(baseUrl))
            {
                throw new ArgumentException($"{nameof(baseUrl)} cannot be empty");
            }

            m_BaseURL = baseUrl;
            Headers = headers ?? new Dictionary<string, string>();

            InitQueriesArgs();
        }

        // [MM] XK - Replace System.Text.Json by Newtonsoft.Json to make plugin compatible with Unity
        public static JsonSerializerSettings CreateJsonSerializerSettings()
        {
            var contractResolver = new CollectionClearingContractResolver()
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


        private UnityWebRequest CreateWebRequest()
        {
            var webRequest = UnityWebRequest.Post(m_BaseURL, UnityWebRequest.kHttpVerbPOST);
            webRequest.SetRequestHeader("Content-Type", "application/json");

            foreach (var header in Headers)
            {
                webRequest.SetRequestHeader(header.Key, header.Value);
            }

            return webRequest;
        }

        public async Task<string> PostAsync(string query)
        {
            OnLog?.Invoke(GetType().ToString() + " - Send Query: " + query);

            var webRequest = CreateWebRequest();

            byte[] postData = Encoding.ASCII.GetBytes(query);
            webRequest.uploadHandler = new UploadHandlerRaw(postData);

            webRequest.SendWebRequest();
            while (!webRequest.isDone)
                await Task.Yield(); // to keep in sync with unity thread

            if (!string.IsNullOrEmpty(webRequest.error))
            {
                var error = webRequest.error;
                webRequest.Dispose();
                OnLog?.Invoke(GetType().ToString() + " - Receive Error: " + error);
                throw new UnityWebRequestException(error, query);
            }

            var res = webRequest.downloadHandler.text;
            webRequest.Dispose();

            OnLog?.Invoke(GetType().ToString() + " - Receive Result: " + res);

            return res;
        }

        public void Dispose()
        {
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