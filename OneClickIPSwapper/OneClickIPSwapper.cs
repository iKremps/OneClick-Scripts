using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Azure.Data.Tables;
using Azure;
using CommonUtilityCode;

namespace OneClickIPSwapper
{
    public class OneClickIPSwapper
    {
        #region table storage objects
        //for connecting to table storage for OneClick table entities
        private static TableServiceClient tableServiceClient;
        private static TableClient table;

        //second table for IPs
        private static TableServiceClient tableServiceClient2;
        private static TableClient table2;
        #endregion


        private dynamic jsonObj;

        public void BaseFunction()
        {
            try
            {
                //get queue table entity
                string part = jsonObj.partKey;
                string row = jsonObj.rowKey;
                TableEntity entity = table.GetEntity<TableEntity>(part, row);


                //get oldest IP
                Pageable<TableEntity> targetIP = table2.Query<TableEntity>();

                Pageable<TableEntity> query = table2.Query<TableEntity>(filter: $"PartitionKey eq 'Not-Used'");

                TableEntity firstIP = null; ;
                foreach (TableEntity item in query)
                {
                    //get only the first IP from the table, change the partition key, break
                    firstIP = item;
                    item.PartitionKey = "Used"; //change partitionKey

                    table2.UpsertEntity(item, TableUpdateMode.Replace); //replace IP with newly changed PartKey

                    break;
                }

                entity["AddressSpace"] = firstIP["value"];

                table.UpsertEntity(entity, TableUpdateMode.Replace); //replaces queue item with new Address
            }
            catch (Exception ex)
            {
                ErrorHandling.throwErrorNormal(ex);
            }

        }

        public void CloudConnections()
        {
            //for queue table
            tableServiceClient = new TableServiceClient(
                new Uri("https://pldevopssacct2.table.core.windows.net/oneclickbuildqueue"),
                new TableSharedKeyCredential("pldevopssacct2", "bzZ2kcshqbMBxPxKSNaNTi89f5CcW+TgPnu8FP8PKf/OCUX1Q9L7Egk/RGbUumDhddQUvxbffl11+AStCjBelA=="));

            table = tableServiceClient.GetTableClient("oneclickbuildqueue");


            //for ip table
            tableServiceClient2 = new TableServiceClient(
                new Uri("https://pldevopssacct2.table.core.windows.net/oneclickavailableIPs"),
                new TableSharedKeyCredential("pldevopssacct2", "bzZ2kcshqbMBxPxKSNaNTi89f5CcW+TgPnu8FP8PKf/OCUX1Q9L7Egk/RGbUumDhddQUvxbffl11+AStCjBelA=="));

            table2 = tableServiceClient2.GetTableClient("oneclickavailableIPs");
        }

        [FunctionName("OneClickIPSwapper")]
        public async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] HttpRequest req,
            ILogger log)
        {
            
            var request = await req.ReadAsStringAsync();
            jsonObj = JsonConvert.DeserializeObject<dynamic>(request);

            CloudConnections();
            BaseFunction();
            return new OkResult();
        }
    }
}
