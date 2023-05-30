using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Microsoft.Azure.Storage.Blob;
using Microsoft.Azure.Storage.Auth;
using Microsoft.Azure.Storage;
using Azure.Storage.Blobs;
using System.Net.Http;
using System.Collections.Generic;
using System.Net.Http.Headers;
using System.Text;
using RestSharp;
using Azure.Data.Tables;
using Newtonsoft.Json.Linq;
using VSI.CloudPlatform.Core.Storage;
using VSI.Model;
using System.Linq;
using VSI.CloudPlaform.Core.Db;
using Azure.Data.Tables.Models;
using CommonUtilityCode;

namespace OneClickJsonUpload
{


    public class OneClickJsonUpload
    {

        #region enviroment variables
        //these enviroment variables can be used if this function is deployed. it will then get the storage account data via config variables.
        //for connecting to storage account and blob container
        private static readonly string connectionString = Environment.GetEnvironmentVariable("connectionString");
            private static readonly string containerName = Environment.GetEnvironmentVariable("containerName");
            private static readonly string storageAccountName = Environment.GetEnvironmentVariable("storageAccountName");
            private static readonly string storageAccountKey = Environment.GetEnvironmentVariable("storageAccountKey");
            private static readonly string storageURI = Environment.GetEnvironmentVariable("storageURI");
            private static readonly string tableName = Environment.GetEnvironmentVariable("tableName");
        #endregion

        #region table storage objects
        //for connecting to table storage for OneClick table entities
        private static TableServiceClient tableServiceClient;
        private static TableClient table;
        #endregion

        #region input json
        //the input
        private static dynamic jsonObj = null;
        #endregion

        #region cloud connection objects
        //objects for cloud connections
        private static BlobContainerClient blobClient;
        private static StorageCredentials credentials;
        private static CloudStorageAccount storageAccount;
        private static CloudBlobClient cloudBlobClient;
        private static CloudBlobContainer blobContainer;
        #endregion

        #region http client for post request
        //http client 
        private static readonly HttpClient client = new HttpClient();
        #endregion

        #region table items RowKey and Partition Key
        private static dynamic entityRowKey;
        private static dynamic entityPartitionKey;
        #endregion


        /// <summary>
        /// Main functionality is done here
        /// </summary>
        public void BaseFunction()
        {
            try
            {               
                cloudConnections();
                insertIntoTable();
                swapIPs();
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine(ex.Message);
                Console.ResetColor();
                Environment.Exit(1);
            }
            
        }


        public void swapIPs()
        {

            dynamic newJsonObj = new
            {
                partKey = entityPartitionKey,
                rowKey = entityRowKey
            };

            string finalJson = JsonConvert.SerializeObject(newJsonObj, Formatting.Indented);


            #region POST request for IP Swap
            var client = new RestClient($"https://pod-dev1-func-oneclickipswapper.azurewebsites.net/api/OneClickIPSwapper?code=V5MPHnR-TP1YGyDgEdYy4Be8yvUk1PPwqo7n6qBs1qb-AzFuAdfsAA==");
            var request = new RestRequest();
            request.Method = Method.Post;
            object j = finalJson; //casts json obj
            request.AddJsonBody(j);

            dynamic response = client.Execute(request);
            #endregion

            Console.WriteLine("- IP is being swapped");
        }

        /// <summary>
        /// Accesses CommonUtilityCode to create OneClickTableEntity object. A table entity will be pulled from this object and added into the table
        /// </summary>
        public void insertIntoTable()
        {
            //uses JSON (jsonObj) to create table entity. WILL HAVE TO MAKE PIPELINEROWKEY A PARAM IN THE FUTURE
            dynamic tableEntity = new OneClickTableEntity(jsonObj);
            entityPartitionKey = tableEntity.PartitionKey;
            entityRowKey = tableEntity.RowKey;

            #region old method of creating table entity
            //string partionKey = DateTime.Now.ToString("M-d-yyyy-T-HH:mm:ss");
            //string rowKey =Guid.NewGuid().ToString();

            //var entity = new TableEntity(partionKey, rowKey)
            //{
            //    { "requestData", jsonObj.ToString()},
            //    { "buildID", 0 },
            //    { "pendingQueue", true },
            //    { "status", "Pending" },
            //    { "pipelineRowKey", "POC-Oneclick" }
            //};

            //table.AddEntity(entity);
            #endregion

            table.AddEntity(tableEntity.Entity);

            Console.WriteLine("Input created as Entity");
        }

        /// <summary>
        /// Converts all entities within the Table to a JSON obj that can be manipulated in-code
        /// </summary>
      /*  public void convertTableEntitiesToJsonObj()
        {
            //get table
            var table = cloudTableClient.GetTableReference("OneClickAPICalls");

            //below fetches all items in table
            TableContinuationToken token = null;
            var entities = new List<dynamic>();
            do
            {
                TableQuery query = new TableQuery();
                var queryResult = table.ExecuteQuerySegmentedAsync(query, token).Result;
                token = queryResult.ContinuationToken;

                foreach (dynamic item in queryResult)
                {
                    entities.Add(item);
                }
            }
            while (token != null);

            //below converts list of table entities into json obj that can be manipulated easily
            var results = new JArray();
            foreach (DynamicTableEntity entity in entities)
            {
                var obj = new JObject();

                obj.Add("PartitionKey", entity.PartitionKey);
                obj.Add("RowKey", entity.RowKey);

                //adds each property to the JObj
                foreach (var p in entity.Properties)
                {
                    obj.Add(p.Key, JToken.FromObject(p.Value.PropertyAsObject));
                }

                results.Add(obj);

            }

            //fill our json obj
            tableEntities = JsonConvert.SerializeObject(results, Formatting.Indented);

        }
      */

        /// <summary>
        /// Makes all connections neccessary for connecting to storage account
        /// </summary>
        public void cloudConnections()
        {
            Console.WriteLine("Creating Cloud Connections...");
            blobClient = new BlobContainerClient(connectionString, containerName);

            credentials = new StorageCredentials(storageAccountName, storageAccountKey);

            storageAccount = new CloudStorageAccount(credentials, useHttps: true);

            cloudBlobClient = storageAccount.CreateCloudBlobClient();

            blobContainer = cloudBlobClient.GetContainerReference(containerName);

            //for table
            tableServiceClient = new TableServiceClient(
                new Uri(storageURI),
                new TableSharedKeyCredential(storageAccountName, storageAccountKey));

                table = tableServiceClient.GetTableClient(tableName);
            
            Console.WriteLine("- Done");
        }

        /// <summary>
        /// Makes sure that all given data is valid
        /// </summary>
        /// <param name="data"></param>
     /*   public void requestValueCheck(dynamic data)
        {
            try
            {
                if(data.storageAccountInformation["connectionString"] == null || data.storageAccountInformation["connectionString"] == "")
                {
                    throw new Exception("The Connection String for the Storage Account is either null or empty");
                }
                else
                {
                    connectionString = data.storageAccountInformation["connectionString"];
                }

                if(data.storageAccountInformation["containerName"] == null || data.storageAccountInformation["containerName"] == "")
                {
                    throw new Exception("The Container name for the Storage Account is either null or empty");
                }
                else
                {
                    containerName = data.storageAccountInformation["containerName"];
                }

                if(data.storageAccountInformation["storageAccountName"] == null || data.storageAccountInformation["storageAccountName"] == "")
                {
                    throw new Exception("The Storage Account Name for the Storage Account is either null or empty");
                }
                else
                {
                    storageAccountName = data.storageAccountInformation["storageAccountName"];
                }

                if(data.storageAccountInformation["storageAccountKey"] == null || data.storageAccountInformation["storageAccountKey"] == "")
                {
                    throw new Exception("The Storage Account Key for the Storage Account is either null or empty");
                }
                else
                {
                    storageAccountKey = data.storageAccountInformation["storageAccountKey"];
                }
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine(ex.Message);
                Console.ResetColor();
                Environment.Exit(1);
            }
        }

        */

        /// <summary>
        /// Function starts here
        /// </summary>
        /// <param name="req"></param>
        /// <returns></returns>
        [FunctionName("OneClickJsonUpload")]
        public async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] HttpRequest req)
        {
            try
            {
                //2 lines below are a more effcient way to obtain input data as a variable 
                var request = await req.ReadAsStringAsync();
                dynamic data = JsonConvert.DeserializeObject<dynamic>(request);
                jsonObj = data; //holds json file to upload

                BaseFunction();
            }
            catch(Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine(ex.Message);
                Console.ResetColor();
                Environment.Exit(1);
            }

            return new OkResult();
        }
    }
}
