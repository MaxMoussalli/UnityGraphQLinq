
using GraphQLinq;
using Newtonsoft.Json;

namespace GraphQLinq.Scaffolding
{
    class Program
    {
        [Serializable]
        public class Test
        {
            public ID myID { get; set; }
            public string strID { get; set; }
        }

        private static void Main(string[] args)
        {
            ID id = "CustomId";
            var tjson = JsonConvert.SerializeObject("CustomId");

            var testData = new Test() { myID = "CustomId", strID = "CustomId" };
            var complexJson = JsonConvert.SerializeObject(testData);
            var json = JsonConvert.SerializeObject(id);


            var dId = JsonConvert.DeserializeObject<ID>(json);
            var testDataDeserialized = JsonConvert.DeserializeObject<Test>(complexJson);

            Console.WriteLine("dId: " + dId);
        }
    } 
}



