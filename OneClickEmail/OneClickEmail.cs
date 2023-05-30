
using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Extensions.Http;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using System.Net.Mail;
using System.Net;
using System.Linq;
using CommonUtilityCode;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Blob;
using Microsoft.WindowsAzure.Storage.Auth;
using System.Data.Common;
using Azure.Data.Tables;
using Azure;

namespace OneClickEmail
{
    public class OneClickEmail
    {

        #region Env Variables
        //private static readonly string SMTP = Environment.GetEnvironmentVariable("SMTP");
        //private static readonly string Username = Environment.GetEnvironmentVariable("testUsername");
        //private static readonly string Password = Environment.GetEnvironmentVariable("Password");
        //private static readonly dynamic PortNumber = Environment.GetEnvironmentVariable("PortNumber");
        //private static readonly dynamic EnableSsl = Environment.GetEnvironmentVariable("EnableSsl");

        //for connecting to storage account where HTML email template is
        private static readonly string storageAccountConnectionString = Environment.GetEnvironmentVariable("storageAccountConnectionString");
        #endregion

        private static dynamic jsonObj;
        private static dynamic recipients;
        private static dynamic emailBody;


        #region Objects for Blob Storage Connection
        StorageCredentials credentials; //access
        CloudStorageAccount storageAccount; //storge account obj
        CloudBlobClient blobClient1; //the client that allows us to access things within the account
        CloudBlobContainer blobContainer; //the container
        CloudBlockBlob blockBlobDestination; //the file

        private static TableServiceClient SmtpEmailConfigsClient;
        private static TableClient SmtpEmailConfigs;
        SMTPCONFIG SMTPSettings = null;
        #endregion

        #region HTML String
        private static string body = ""; //body of the email. will be a template that can be modified
        #endregion


        public void BaseFuncton()
        {
            try
            {
                var smtpClient = new SmtpClient(SMTPSettings.Host) //host of server
                {
                    Port = int.Parse(SMTPSettings.PortNumber), //config
                    Credentials = new NetworkCredential(SMTPSettings.UserName, SMTPSettings.Password),
                    EnableSsl = SMTPSettings.EnableSSL, //config
                };


                var mailMessage = new MailMessage
                {
                    From = new MailAddress(SMTPSettings.FromEmail), //email of sender
                    Subject = $"{jsonObj.RgName} Environment Completed",
                    Body = emailBody,
                    IsBodyHtml = true,
                };

                mailMessage.To.Add(SMTPSettings.ToEmail); //send to dev ops team
                                                          //mailMessage.To.Add("osama.amirkhan@systemsltd.com"); //send to dev ops team

                //mailMessage.To.Add(recipients); //email of recipient


                smtpClient.Send(mailMessage);
            }
            catch (Exception ex)
            {
                ErrorHandling.throwErrorNormal(ex);
            }
        }


        public void CloudConnections()
        {
            var bobTheBuilder = new DbConnectionStringBuilder { ConnectionString = storageAccountConnectionString };
            bobTheBuilder.TryGetValue("AccountName", out dynamic accountName);
            bobTheBuilder.TryGetValue("AccountKey", out dynamic accountKey);

            credentials = new StorageCredentials(accountName, accountKey);
            storageAccount = new CloudStorageAccount(credentials, true);
            blobClient1 = storageAccount.CreateCloudBlobClient();
            blobContainer = blobClient1.GetContainerReference("script-testing");
            blockBlobDestination = blobContainer.GetBlockBlobReference($"emailTemplate.html");
            emailBody = blockBlobDestination.DownloadTextAsync().Result;

            //for SMTP table
            SmtpEmailConfigsClient = new TableServiceClient(
                new Uri("https://pldevopssacct2.table.core.windows.net/SMTPEmailConfiguration"),
                new TableSharedKeyCredential("pldevopssacct2", "bzZ2kcshqbMBxPxKSNaNTi89f5CcW+TgPnu8FP8PKf/OCUX1Q9L7Egk/RGbUumDhddQUvxbffl11+AStCjBelA=="));

            SmtpEmailConfigs = SmtpEmailConfigsClient.GetTableClient("SMTPEmailConfiguration");

            //Getting SMTP Configuration
            Pageable<TableEntity> targetIP = SmtpEmailConfigs.Query<TableEntity>();

            Pageable<TableEntity> query = SmtpEmailConfigs.Query<TableEntity>(filter: $"PartitionKey eq 'OneClickEmail'");
            TableEntity results = SmtpEmailConfigs.Query<TableEntity>(filter: $"PartitionKey eq 'OneClickEmail'").FirstOrDefault();

            var obj = JsonConvert.SerializeObject(results);
            SMTPSettings = JsonConvert.DeserializeObject<SMTPCONFIG>(obj);
        }


        [FunctionName("OneClickEmail")]
        public async Task<IActionResult> Run(
            [HttpTrigger(AuthorizationLevel.Function, "get", "post", Route = null)] HttpRequest req,
            ILogger log)
        {
            var request = await req.ReadAsStringAsync();
            jsonObj = JsonConvert.DeserializeObject<dynamic>(request);

            //recipients = string.Join(", ", jsonObj.Recipients); //converts list of recipients into string that is viable for code

            CloudConnections();
            BaseFuncton();

            return new OkResult();
        }
        public class SMTPCONFIG
        {

            public string PartitionKey { get; set; }
            public string RowKey { get; set; }
            public string UserName { get; set; }
            public string Password { get; set; }
            public string PortNumber { get; set; }
            public string ToEmail { get; set; }
            public string FromEmail { get; set; }
            public string Host { get; set; }
            public bool EnableSSL { get; set; }
        }
    }
}