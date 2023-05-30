# OneClick-Scripts
Azure Function App scripts that were utilized in project OneClick. This project allowed the creation of an entire Azure environment along with essential resources (database/azure functions/key vault/VNET/etc), all with a single POST request. These scripts in this directory were my contribution to the project, all other scripts not included were created by my DevOps team members.

General Overview of each Script:

OneClickJsonUpload: Accepts JSON payload as input. This JSON includes all information that is required for creating a new Azure Environment (RG). The JSON is stored into a table on an Azure storage account. An IP value is chosed from a list of hundreds of IPs and added onto the table entity. This IP is gaurenteed to be unique for the creation of the new Azure Environment.

OneClickIPSwapper: After the initial JSON payload is uploaded to storage, this function is called to pull an avilable unique IP address to be stored into the JSON entity. After an IP is used, it is marked as 'used' and not used again for future environments.

OneClickIPUploader: This simple script adds additional IPs into the IP table in Azure Storage if all IPs end up being used. A simple file is read and those IPs are added.

OneClickPipelineBuild: A timer function that runs every 5 min. This function will enter the table where all JSON payloads were sent, and check for the most recent entity that has a "PendingQueue" property value of "true". This indicates that the specified table entity/request has not been processed yet. This will send the specified entity JSON to an AzureDevOps Pipeline where the creation process will begin.

OneClick Email: After the entire OneClick process is completed, an email is sent to the specified address that the environment is ready. This can easily be enhanced/configured differently.

