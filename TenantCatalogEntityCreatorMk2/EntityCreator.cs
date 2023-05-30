using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using CommonUtilityCode;
using Azure.Data.Tables;
using System.Data.Common;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.Extensibility;
using VSI.CloudPlatform.Core.Telemetry;
using VSI.CloudPlatform.Core.Functions;

namespace TenantCatalogEntityCreatorMk2
{
    public static class EntityCreator
    {
        private static readonly string _key = Environment.GetEnvironmentVariable("APPINSIGHTS_INSTRUMENTATIONKEY");
        private static bool _excludeDependency = FunctionUtilities.GetBoolValue(Environment.GetEnvironmentVariable("ExcludeDependency"), false);



        [FunctionName("EntityCreator")]
        public static async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] HttpRequest req,
            ILogger log)
        {

            TelemetryClient telemetryClient = TelemetryFactory.GetInstance("EntityCreator", _key, _excludeDependency);
            IOperationHolder<RequestTelemetry> operation = telemetryClient.StartOperation<RequestTelemetry>("EntityCreator", Guid.NewGuid().ToString());

            try
            {

                var request = await req.ReadAsStringAsync();
                dynamic jsonObj = JsonConvert.DeserializeObject<dynamic>(request);

                TableClient table = tableConnection(jsonObj);

                dynamic tableEntity = new TenantCatalogEntity(jsonObj, true);
                TableEntity entity = tableEntity.Entity;

                table.AddEntity(entity);
            }
            catch (Exception ex)
            {
                if (telemetryClient != null)
                {
                    telemetryClient.TrackException(ex);
                    telemetryClient.StopOperation(operation);
                }
                throw;
            }
            

            return new OkResult();
        }



        public static TableClient tableConnection(dynamic jsonObj)
        {
            TableClient tableClient = null;

            try
            {
                Console.WriteLine("Connecting to Table...");

                //we use the DbConnectionStringBuilder to pull account name and key values from table connection string
                var builder = new DbConnectionStringBuilder { ConnectionString = jsonObj.commonstorageConnectionString };
                builder.TryGetValue("AccountName", out dynamic accountName);
                builder.TryGetValue("AccountKey", out dynamic accountKey);

                tableClient = new TableClient(
                    new Uri($"https://{accountName}.table.core.windows.net/tenantCatalog"),
                    "tenantCatalog",
                    new TableSharedKeyCredential(accountName, accountKey));

                Console.WriteLine("- Connected");

            }
            catch (Exception ex)
            {
                ErrorHandling.throwErrorNormal(ex);
            }

            return tableClient;
        }


    }
}
