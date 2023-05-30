using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using Azure;
using Azure.Data.Tables;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using RestSharp;

namespace OneClickPipelineBuild
{
    public class OneClickPipelineBuild
    {

        //enviroment vars
        private static readonly string storageAccountName = Environment.GetEnvironmentVariable("storageAccountName");
        private static readonly string storageAccountKey = Environment.GetEnvironmentVariable("storageAccountKey");
        private static readonly string storageURI = Environment.GetEnvironmentVariable("storageURI");
        private static readonly string tableName = Environment.GetEnvironmentVariable("tableName");
        private static readonly string tableNamePipelines = Environment.GetEnvironmentVariable("tableNamePipelines");
        private static readonly string storageURIPipeLines = Environment.GetEnvironmentVariable("storageURIPipeLines");

        //table
        private static TableServiceClient tableServiceClient;
        private static TableServiceClient tableServiceClient2;
        private static TableClient table;
        private static TableClient tableOfPipeLines;

        public void BaseFunction()
        {
            try
            {

                //this function will go through the build queue table, and check the build status of all entities
                checkBuildStatus();


                //create call for getting pipelinetable up here out of foreach
                Pageable<TableEntity> targetPipeline = tableOfPipeLines.Query<TableEntity>();

                Pageable<TableEntity> query = table.Query<TableEntity>(filter: $"pendingQueue eq true");

                foreach (TableEntity tableEntity in query)
                {
                    //below code fetches the definitionID of the pipeline to make the correct post request
                    dynamic pipeLineRowKey = "";
                    tableEntity.TryGetValue("pipelineRowKey", out pipeLineRowKey);

                    //get buildID of pipeline to modify request URL
                    //Pageable<TableEntity> targetPipeline = tableOfPipeLines.Query<TableEntity>(filter: $"PartitionKey eq '{(string)pipeLineRowKey}'");
                    dynamic pipelineDefinitionID = 0;

                    TableEntity results = tableOfPipeLines.Query<TableEntity>(filter: $"PartitionKey eq '{pipeLineRowKey}'").FirstOrDefault();
                    ((TableEntity)results).TryGetValue("definitionID", out pipelineDefinitionID);

                    #region old method or retriving data from piplinetable
                    //IEnumerator e = targetPipeline.GetEnumerator();
                    //while (e.MoveNext())
                    //{
                    //    var v = e.Current;

                    //    //if partition key matches, get def id
                    //    if(((TableEntity)v).PartitionKey == pipeLineRowKey)
                    //    {
                    //        ((TableEntity)v).TryGetValue("definitionID", out pipelineDefinitionID);
                    //        break;
                    //    }

                    //}
                    #endregion

                    //make post reuest to build pipeline
                    var client = new RestClient($"https://dev.azure.com/visionet-davinci/DevOps/_apis/build/builds?definitionId={pipelineDefinitionID}&api-version=7.1-preview.7");
                    var request = new RestRequest();
                    request.Method = Method.Post;
                    request.AddHeader("Authorization", "Basic OmptYWF2aG5zdG9lZXl6ZWp1eHhua3VweTMzZWtobXU3dWFib2RjZmk0N2tnc3BudGY0aXE=");
                    request.AddHeader("Content-Type", "application/json");
                    //request.AddHeader("Cookie", "VstsSession=%7B%22PersistentSessionId%22%3A%22a924d8ff-665d-42dd-8b1b-ea589f1570ab%22%2C%22PendingAuthenticationSessionId%22%3A%2200000000-0000-0000-0000-000000000000%22%2C%22CurrentAuthenticationSessionId%22%3A%2200000000-0000-0000-0000-000000000000%22%2C%22SignInState%22%3A%7B%7D%7D");

                    dynamic response = client.Execute(request);

                    Console.WriteLine("- Pipeline is being Built");

                    //now we will change the variables in the queue table
                    response = ((RestSharp.RestResponseBase)response).Content;
                    var tester = JsonConvert.DeserializeObject<dynamic>(response);


                    tableEntity["pendingQueue"] = false;
                    tableEntity["buildID"] = (int)tester.id;
                    tableEntity["status"] = "Building";
                    //tableEntity["Timestamp"] = null;

                    table.UpsertEntity(tableEntity, TableUpdateMode.Replace);

                    //end of first queued item
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


        /// <summary>
        /// Creates a query to get all pipeline table entities with "Building" status. Their build status is then checked and updated if it is completed.
        /// </summary>
        public void checkBuildStatus()
        {
            Console.WriteLine("Checking All Build Statuses Before Running/Queuing more Pipelines...");

            Pageable<TableEntity> query = table.Query<TableEntity>(filter: $"status eq 'Building'");

            foreach(TableEntity tableEntity in query)
            {
                //gets buildID to modify request URL 
                dynamic targetBuildID = "";
                foreach (var p in tableEntity)
                {
                    if (p.Key == "buildID")
                    {
                        targetBuildID = p.Value;
                        break;
                    }
                }

                //configure request
                var client = new RestClient($"https://dev.azure.com/visionet-davinci/DevOps/_apis/build/builds/{targetBuildID}?api-version=6.0");
                var request = new RestRequest();
                request.Method = Method.Get;
                request.AddHeader("Authorization", "Basic OnRtamxqb3RwemJ3Z29kZ2dlZGhzdzM3N3pweG1wemJvdXd6anFqczZnN2dhcWhoNWpsZXE=");
                request.AddHeader("Content-Type", "application/json");
                request.AddHeader("Cookie", "VstsSession=%7B%22PersistentSessionId%22%3A%22a924d8ff-665d-42dd-8b1b-ea589f1570ab%22%2C%22PendingAuthenticationSessionId%22%3A%2200000000-0000-0000-0000-000000000000%22%2C%22CurrentAuthenticationSessionId%22%3A%2200000000-0000-0000-0000-000000000000%22%2C%22SignInState%22%3A%7B%7D%7D");

                //response of request
                dynamic response = client.Execute(request);
                response = ((RestSharp.RestResponseBase)response).Content;
                var tester = JsonConvert.DeserializeObject<dynamic>(response);

                //updates status in table based on response
                tableEntity["status"] = (string)tester.result;               

                //upsert updates the table entity with the new status
                table.UpsertEntity(tableEntity, TableUpdateMode.Replace);

                Console.WriteLine(" - Build Status Updated");
            }
        }


        public void cloudConnections()
        {
            tableServiceClient = new TableServiceClient(
                new Uri(storageURI),
                new TableSharedKeyCredential(storageAccountName, storageAccountKey));
            table = tableServiceClient.GetTableClient(tableName);
            

            tableServiceClient2 = new TableServiceClient(
                new Uri(storageURIPipeLines),
                new TableSharedKeyCredential(storageAccountName, storageAccountKey));
            tableOfPipeLines = tableServiceClient2.GetTableClient(tableNamePipelines);
        }



        [FunctionName("OneClickPipelineBuild")]
        public void Run([TimerTrigger("0 */5 * * * *")]TimerInfo myTimer, ILogger log) //runs every 5 min
        {
            log.LogInformation($"C# Timer trigger function executed at: {DateTime.Now}");
            Console.WriteLine("- Function has Triggered");

            cloudConnections();
            BaseFunction();

        }
    }
}
