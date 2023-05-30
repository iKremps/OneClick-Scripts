using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection.Metadata.Ecma335;
using System.Threading.Tasks;
using Microsoft.ApplicationInsights;
using Microsoft.ApplicationInsights.DataContracts;
using Microsoft.ApplicationInsights.Extensibility;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Extensions.Logging;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using Microsoft.WindowsAzure.Storage.File;
using Microsoft.WindowsAzure.Storage.Table;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using VSI.CloudPlatform.Core.Functions;
using VSI.CloudPlatform.Core.Telemetry;

namespace DataCopyUtility
{
    public class ImportStartEventArgs : EventArgs
    {
        public int Count { get; set; }
    }
    public class RecordImportedEventArgs : EventArgs
    {
        public int RecordNumber { get; set; }
    }
    public class DataCopyUtility
    {
        public string sourceConnectionString = string.Empty;
        public string targetConnectionString = string.Empty;
        private string sourceStorageAccount = string.Empty;
        private string targetStorageAccount = string.Empty;
        private static bool Containers = false;
        private static bool FileShares = false;
        private static bool Tables = false;

        public delegate void ImportStartEventHandler(object sender, ImportStartEventArgs e);
        public delegate void RecordImportedEventHandler(object sender, RecordImportedEventArgs e);
        public event ImportStartEventHandler ImportStart;
        public event RecordImportedEventHandler RecordImported;


        //telemtry clients for monitoring/logging
        public IOperationHolder<RequestTelemetry> operation;
        public TelemetryClient telemetryClient;
        private static readonly string _key = Environment.GetEnvironmentVariable("APPINSIGHTS_INSTRUMENTATIONKEY");
        private static bool _excludeDependency = FunctionUtilities.GetBoolValue(Environment.GetEnvironmentVariable("ExcludeDependency"), false);



        [Timeout("10:00:00")]
        [FunctionName("DataCopyUtility")]
        public async void Run([HttpTrigger(AuthorizationLevel.Function, "post", Route = null)] HttpRequest req, ILogger log)
        {
            try
            {
                log.LogInformation($"C# Timer trigger function executed at: {DateTime.Now}");

                
                telemetryClient = TelemetryFactory.GetInstance("DBMigrationDataChange", _key, _excludeDependency); //creates an instance of a telemetry and connects it to the function given its name/key
                operation = telemetryClient.StartOperation<RequestTelemetry>("DBMigrationDataChange", Guid.NewGuid().ToString());

                var request = req.ReadAsStringAsync().Result;
                dynamic data = JsonConvert.DeserializeObject<dynamic>(request);

                setInputData(data);

                if (Containers)
                {
                    ContainerMovement().Wait();
                }

                if (FileShares)
                {
                    FileSharesMovement().Wait();
                }

                if (Tables)
                {
                    TablesMovement();
                }

            }
            catch(Exception ex)
            {
                if (telemetryClient != null)
                {
                    telemetryClient.TrackException(ex);
                    telemetryClient.StopOperation(operation);
                }
                throw;
            }

            

        }

        public void setInputData(dynamic data)
        {
            sourceConnectionString = data.SourceStorageConnetionString;
            targetConnectionString = data.TargetStorageConnetionString;
            sourceStorageAccount = data.SourceStorageAccount;
            targetStorageAccount = data.TargetStorageAccount;
            Containers = data.Containers;
            FileShares = data.FileShares;
            Tables = data.Tables;
        }

        #region Containers Movement
        public async Task ContainerMovement()
        {
            try
            {
                CloudStorageAccount source = CloudStorageAccount.Parse(sourceConnectionString);
                CloudStorageAccount target = CloudStorageAccount.Parse(targetConnectionString);
                CloudBlobClient sourceCloudBlobClient = source.CreateCloudBlobClient();
                CloudBlobClient targetCloudBlobClient = target.CreateCloudBlobClient();

                var allContainers = sourceCloudBlobClient.ListContainersSegmentedAsync(null).Result;
                var blobContainers = allContainers.Results.ToList();
                foreach (var container in blobContainers)
                {
                    //ADD IF STATEMENT HERE IF YOU WANT TO EXCLUDE CERTAIN CONTAINERS
                    if (container.Name != "archive")
                    {

                        var result = await container.CreateIfNotExistsAsync();
                        bool isExist = await container.ExistsAsync();
                        if (!isExist)
                        {
                            container.CreateAsync().Wait();
                        }
                        //Task createDirectory = targetCloudBlobClient.GetContainerReference(container.Name).CreateIfNotExistsAsync();
                        CloudBlobContainer sCloudBlobContainer = sourceCloudBlobClient.GetContainerReference(container.Name);
                        CloudBlobContainer tCloudBlobContainer = targetCloudBlobClient.GetContainerReference(container.Name);
                        //await tCloudBlobContainer.CreateIfNotExistsAsync();
                        var allFiles = sCloudBlobContainer.ListBlobsSegmentedAsync(null).Result;
                        var blobFiles = allFiles.Results.ToList();

                        foreach (var item in blobFiles)
                        {

                            if (item is CloudBlob)
                            {
                                Move(item, sCloudBlobContainer, tCloudBlobContainer, telemetryClient, operation).Wait();
                            }
                            if (item is CloudBlobDirectory)
                            {
                                Recursion(item, sCloudBlobContainer, tCloudBlobContainer, telemetryClient, operation);
                            }
                        }
                    }
                }
                //Console.Read();
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
            
        }
        private static void Recursion(dynamic folder, CloudBlobContainer source, CloudBlobContainer target, TelemetryClient telemetryClient, IOperationHolder<RequestTelemetry> operation)
        {
 
            CloudBlobDirectory cloudBlobDirectory = source.GetDirectoryReference(folder.Prefix);
            //true for all sub directories else false
            var rootDirFolders = cloudBlobDirectory.ListBlobsSegmentedAsync(null).Result;
            var FilesURL = rootDirFolders.Results.ToList();
            foreach (var item in FilesURL)
            {
                if (item is CloudBlob || item is CloudAppendBlob)
                {
                    Move(item, source, target, telemetryClient, operation).Wait();
                }

                if (item is CloudBlobDirectory)
                {
                    Recursion(item, source, target, telemetryClient, operation);
                }
            }
     
        }
        public static async Task Move(dynamic file, CloudBlobContainer source, CloudBlobContainer target, TelemetryClient telemetryClient, IOperationHolder<RequestTelemetry> operation)
        {
            try
            {
                string blobName = file.Name;
                //Create container into blob if not exists
                await target.CreateIfNotExistsAsync();
                Console.WriteLine("Started moving blob: " + blobName + " from container " + source.Name + " to " + target.Name);
                CloudBlockBlob sourceBlob = source.GetBlockBlobReference(blobName);
                CloudBlockBlob targetBlob = target.GetBlockBlobReference(blobName);
                try
                {
                    using (var sourceStream = await sourceBlob.OpenReadAsync())
                    using (var destStream = await targetBlob.OpenWriteAsync())
                    {
                        await sourceStream.CopyToAsync(destStream);
                    }
                }
                catch
                {
                    CloudAppendBlob appendBlob = source.GetAppendBlobReference(blobName);
                    var snapshot = await appendBlob.CreateSnapshotAsync();
                    var text = await snapshot.DownloadTextAsync();
                    await targetBlob.UploadTextAsync(text);
                }

                if (targetBlob.ExistsAsync().Result)
                {
                    //Delete blob from source container
                    //await sourceBlob.DeleteAsync();
                }
                else
                {
                    Console.WriteLine("This line is getting empty");
                }
                Console.WriteLine(blobName + " blob has been moved successfully.");
            }
            catch(Exception ex)
            {
                if (telemetryClient != null)
                {
                    telemetryClient.TrackException(ex);
                    telemetryClient.StopOperation(operation);
                }
                throw;
            }
            
        }
        #endregion

        #region File Shares Movement
        public async Task FileSharesMovement()
        {
            try
            {
                CloudStorageAccount scloudStorageAccount = CloudStorageAccount.Parse(sourceConnectionString);
                CloudStorageAccount tcloudStorageAccount = CloudStorageAccount.Parse(targetConnectionString);
                CloudFileClient scloudFileClient = scloudStorageAccount.CreateCloudFileClient();
                CloudFileClient tcloudFileClient = tcloudStorageAccount.CreateCloudFileClient();

                var allFileShares = scloudFileClient.ListSharesSegmentedAsync(null).Result;
                var fileShares = allFileShares.Results.ToList();
                //var res = fileShares.Take(3);
                foreach (var fileShare in fileShares)
                {
                    //await fileShare.CreateIfNotExistsAsync();
                    CloudFileShare sourceFileShare = scloudFileClient.GetShareReference(fileShare.Name);
                    await tcloudFileClient.GetShareReference(fileShare.Name).CreateIfNotExistsAsync();
                    //CloudFileShare targetFileShare = tcloudFileClient.GetShareReference(fileShare.Name);
                    CloudFileShare targetFileShare = tcloudFileClient.GetShareReference(fileShare.Name);

                    //await fileShare.CreateIfNotExistsAsync();
                    var fileList = sourceFileShare.GetRootDirectoryReference().ListFilesAndDirectoriesSegmentedAsync(null).Result;
                    var FilesURL = fileList.Results.ToList();
                    foreach (var item in FilesURL)
                    {
                        if (item is CloudFile)
                        {
                            Move_CloudFileShare(item, sourceFileShare, targetFileShare, telemetryClient, operation).Wait();
                        }
                        if (item is CloudFileDirectory)
                        {
                            Recursion_CloudFileShare(item, sourceFileShare, targetFileShare, telemetryClient, operation).Wait();
                        }
                    }
                }
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

            //Console.Read();
        }
        public static async Task Recursion_CloudFileShare(dynamic folder, CloudFileShare source, CloudFileShare target, TelemetryClient telemetryClient, IOperationHolder<RequestTelemetry> operation)
        {
            try
            {
                string folername = folder.Name;
                await target.GetRootDirectoryReference().GetDirectoryReference(folername).CreateIfNotExistsAsync();

                CloudFileDirectory sourceFolder = source.GetRootDirectoryReference().GetDirectoryReference(folername);
                var sourcefileList = sourceFolder.ListFilesAndDirectoriesSegmentedAsync(null).Result;

                var targetFolders = target.GetRootDirectoryReference().GetDirectoryReference(folername);

                var FilesURL = sourcefileList.Results;

                foreach (var item in FilesURL)
                {
                    if (item is CloudFile)
                    {
                        await Move_CloudFileDirectory(item, sourceFolder, targetFolders, telemetryClient, operation);
                        Console.WriteLine("Started moving File: " + item + " from container " + sourceFolder.Name + " to " + targetFolders.Name);

                        Console.WriteLine(item + " blob moved has been successful.");
                    }
                    if (item is CloudFileDirectory)
                    {
                        if (targetFolders.ExistsAsync().Result)
                        {
                            await Recursion_CloudFileDirectory(item, sourceFolder, targetFolders, telemetryClient, operation);
                        }
                    }
                }
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
        }
        public static async Task Recursion_CloudFileDirectory(dynamic folder, CloudFileDirectory source, CloudFileDirectory target, TelemetryClient telemetryClient, IOperationHolder<RequestTelemetry> operation)
        {
            try
            {
                string folername = folder.Name;
                await target.GetDirectoryReference(folername).CreateIfNotExistsAsync();

                var targetFolders = target.GetDirectoryReference(folername);
                CloudFileDirectory sourceFolder = source.GetDirectoryReference(folername);

                var sourcefileList = sourceFolder.ListFilesAndDirectoriesSegmentedAsync(null).Result;
                var FilesURL = sourcefileList.Results;

                foreach (var item in FilesURL)
                {
                    if (item is CloudFile)
                    {
                        await Move_CloudFileDirectory(item, sourceFolder, targetFolders, telemetryClient, operation);
                    }
                    if (item is CloudFileDirectory)
                    {
                        await Recursion_CloudFileDirectory(item, sourceFolder, targetFolders, telemetryClient, operation);
                    }
                }
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

        }
        public static async Task Move_CloudFileDirectory(dynamic file, CloudFileDirectory source, CloudFileDirectory target, TelemetryClient telemetryClient, IOperationHolder<RequestTelemetry> operation)
        {
            try
            {
                string fileName = file.Name;
                CloudFile sourceFile = source.GetFileReference(fileName);
                CloudFile targetFile = target.GetFileReference(fileName);

                using (var targetBlobStream = await targetFile.OpenWriteAsync(file.Properties.Length))
                {
                    using (var sourceBlobStream = await file.OpenReadAsync())
                    {
                        await sourceBlobStream.CopyToAsync(targetBlobStream);
                    }

                    if (targetFile.ExistsAsync().Result)
                    {
                        //Delete blob from source container 
                        //await sourceFile.DeleteAsync();
                    }
                }
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

        }
        public static async Task Move_CloudFileShare(dynamic file, CloudFileShare source, CloudFileShare target, TelemetryClient telemetryClient, IOperationHolder<RequestTelemetry> operation)
        {
            try
            {
                string fileName = file.Name;
                Console.WriteLine("Started moving files: " + fileName + " from File Shares " + source.Name + " to " + target.Name);
                CloudFile sourceFile = source.GetRootDirectoryReference().GetFileReference(fileName);
                CloudFile targetFile = target.GetRootDirectoryReference().GetFileReference(fileName);

                using (var targetBlobStream = await targetFile.OpenWriteAsync(file.Properties.Length))
                {
                    using (var sourceBlobStream = await sourceFile.OpenReadAsync())
                    {
                        await sourceBlobStream.CopyToAsync(targetBlobStream);
                    }
                    if (targetFile.ExistsAsync().Result)
                    {
                        //Delete blob from source container 
                        //await sourceFile.DeleteAsync();
                    }
                }
                Console.WriteLine(fileName + " blob moved has been successful.");
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
        }
        #endregion

        #region Tables Movement
        public List<string> TablesMovement()
        {
            CloudStorageAccount sConnection = CloudStorageAccount.Parse(sourceConnectionString);
            CloudTableClient sourceCloudTableClient = sConnection.CreateCloudTableClient();

            var tableList = new List<string>();
            var AllFiles = sourceCloudTableClient.ListTablesSegmentedAsync(null).Result;
            var FilesURL = AllFiles.Results.ToList();

            foreach (var table in FilesURL)
            {
                JArray result = ReadData(table.Name);
                Console.WriteLine("Started moving Tables: " + table.Name + " from Storage Account " + sourceStorageAccount + " to " + targetStorageAccount);
                WriteData(table.Name, result);
                Console.WriteLine(table.Name + " has been moved successfully.");
            }

            return tableList;
        }
        public JArray ReadData(string tableName)
        {
            CloudStorageAccount sConnection = CloudStorageAccount.Parse(sourceConnectionString);
            CloudTableClient sourceCloudTableClient = sConnection.CreateCloudTableClient();

            var tables = sourceCloudTableClient.GetTableReference(tableName);
            var results = new JArray();
            var query = new TableQuery();
            var data = tables.ExecuteQuerySegmentedAsync(query, null).Result;

            if (data.Any())
            {
                foreach (var item in data)
                {
                    var obj = new JObject();

                    obj.Add("PartitionKey", item.PartitionKey);
                    obj.Add("RowKey", item.RowKey);

                    foreach (var p in item.Properties)
                    {
                        obj.Add(p.Key, JToken.FromObject(p.Value.PropertyAsObject));
                    }

                    results.Add(obj);
                }
            }
            return results;
        }
        public void WriteData(string fileName, JArray data)
        {
            try
            {
                CloudStorageAccount tConnection = CloudStorageAccount.Parse(targetConnectionString);
                CloudTableClient targetCloudTableClient = tConnection.CreateCloudTableClient();

                var table = targetCloudTableClient.GetTableReference(fileName);
                var recordCount = data.Children().Count();
                var currentRecord = 0;

                ImportStart?.Invoke(this, new ImportStartEventArgs() { Count = recordCount });

                table.CreateIfNotExistsAsync();

                foreach (JToken itemArry in data.Children())
                {
                    var entity = new DynamicTableEntity();
                    var tableOperation = TableOperation.InsertOrReplace(entity);

                    foreach (JToken item in itemArry.Children())
                    {
                        var name = ((JProperty)item).Name;
                        var value = Convert.ToString(((JValue)item.First()).Value);

                        if (name == "PartitionKey")
                        {
                            entity.PartitionKey = value;
                        }
                        else if (name == "RowKey")
                        {
                            entity.RowKey = value;
                        }
                        else
                        {
                            if ((name == "XSLT_Path" || name == "EDISchemaPath" || name == "EDISpecificationPath") && !string.IsNullOrEmpty(sourceStorageAccount))
                            {
                                value = value.Replace(sourceStorageAccount, targetStorageAccount);
                            }

                            entity.Properties.Add(name, new EntityProperty(value));
                        }
                    }

                    table.ExecuteAsync(tableOperation);

                    currentRecord += 1;

                    RecordImported?.Invoke(this, new RecordImportedEventArgs() { RecordNumber = currentRecord });
                }
            }
            catch(Exception ex)
            {
                if (telemetryClient != null)
                {
                    telemetryClient.TrackException(ex);
                    telemetryClient.StopOperation(operation);
                }
                throw;
            }
            
        }
        #endregion
    }
}
