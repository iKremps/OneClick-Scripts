# OneClick-Scripts
Azure Function App scripts that were utilized in project OneClick. This project allowed the creation of an entire Azure environment along with essential resources (database/azure functions/key vault/VNET/etc), all with a single POST request. These scripts in this directory were my contribution to the project, all other scripts not included were created by my DevOps team members.

A majority of these scripts are called via POST requests. An AzureDevOps Pipeline utilizes some of these scripts, where it uses information passed from previous tasks in the pipeline to create the JSON bodies for the POST requests. One of my team members were mainly responsible for creating this Pipeline, but I assisted them when it needed to call my scripts.

For an example of a JSON payload for a specific function, check out the "Example-Payloads" folder. If there is no example for a specific function, then that function does not need to make a POST request to work.

General Overview of each Script:

OneClickJsonUpload: Accepts JSON payload as input. This JSON includes most information that is required for creating a new Azure Environment (RG). The JSON is stored into a table on an Azure storage account. An IP value is chosed from a list of hundreds of IPs and added onto the table entity. This IP is gaurenteed to be unique for the creation of the new Azure Environment.

OneClickIPSwapper: After the initial JSON payload is uploaded to storage, this function is called to pull an avilable unique IP address to be stored into the JSON entity. After an IP is used, it is marked as 'used' and not used again for future environments.

OneClickIPUploader: This simple script adds additional IPs into the IP table in Azure Storage if all IPs end up being used. A simple file is read and those IPs are added.

OneClickPipelineBuild: A timer function that runs every 5 min. This function will enter the table where all JSON payloads were sent, and check for the most recent entity that has a "PendingQueue" property value of "true". This indicates that the specified table entity/request has not been processed yet. This will send the specified entity JSON to an AzureDevOps Pipeline where the creation process will begin.

DBMigrationDataChange: When the AzureDevOps Pipeline created a new (and empty) Cosmos Database for the environment, this script is called to migrate default/client specific data into it. A default Cosmos DB is accessed in Azure to pull the default data from. Queries are used against the default DB to ensure no unnecessary data is fetched. After this data has been migrated, specific containers are accessed because certain data needs to be add/altered to match the new Environment. Some of this data includes client ID, and most importantly Azure Function URL Information. Some Azure Functions are created specifically for an Anzure environment, thus making each function unique with their own URL. DBs need to be populated with their respective Azure Function URLs, not the default data URLs. These values are adjusted. This is done by accessing an Azure table that contains all information regarding all Azure Functions for all environments. Data (the URLs) are pulled from this table.

DataCopyUtility: After Cosmos DB migration and data changes are completed, the default data for the Environment's Azure Storage Account is migrated. This includes Blob Containers, Fileshares, and Tables (can be configured in the JSON payload input).

OneClick Email: After the entire OneClick process is completed, an email is sent to the specified address that the environment is ready. This can easily be enhanced/configured differently.

