
using System.Collections;
using Azure.Data.Tables;

namespace OneClickIPUploader 
{
    public partial class OneClickIPUloader
    {

        private static readonly List<string> listOfIPs = new List<string>();
        #region table storage objects
        //for connecting to table storage for OneClick table entities
        private static TableServiceClient tableServiceClient;
        private static TableClient table;
        #endregion

        public static void Main()
        {
            BaseFunction();
        }


        public static void BaseFunction()
        {
            int counter = 0;
            string line;
            StreamReader file = new StreamReader("C:\\Users\\ian.krempa\\Source\\Repos\\Utilities\\Solutions\\CommonUtilities\\OneClickIPUploader\\ips.txt");

            while ((line = file.ReadLine()) != null)
            {
                listOfIPs.Add(line);
            }

            file.Close();

            //for table
            tableServiceClient = new TableServiceClient(
                new Uri("https://pldevopssacct2.table.core.windows.net/oneclickavailableIPs"),
                new TableSharedKeyCredential("pldevopssacct2", "bzZ2kcshqbMBxPxKSNaNTi89f5CcW+TgPnu8FP8PKf/OCUX1Q9L7Egk/RGbUumDhddQUvxbffl11+AStCjBelA=="));

            table = tableServiceClient.GetTableClient("oneclickavailableIPs");


            foreach(var ip in listOfIPs)
            {
                TableEntity entity = new TableEntity();

                entity.Add("value", ip);
                entity.RowKey = Guid.NewGuid().ToString();
                entity.PartitionKey = "Not-Used";

                table.AddEntityAsync(entity).GetAwaiter().GetResult();
                Console.WriteLine($"Added new ip: {ip}");
            }

            Console.WriteLine("All ips added :)");

        }

    }
}