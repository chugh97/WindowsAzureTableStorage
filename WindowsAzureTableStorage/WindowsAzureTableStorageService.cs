﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Diagnostics;
using Microsoft.WindowsAzure.Storage;
using Microsoft.WindowsAzure.Storage.Auth;
using Microsoft.WindowsAzure.Storage.Table;

namespace WindowsAzureTableStorage
{
    /// <summary>
    /// Utility class for connecting to and storing data in Windows Azure Table Storage
    /// </summary>
    public class WindowsAzureTableStorageService
    {
        string storageConnectionString = "";
        CloudTableClient tableClient = null;
        private List<Task> batchTasks;

        /// <summary>
        /// Default constructor - no initialization of Azure is done before calling a specific method.
        /// </summary>
        /// <param name="StorageConnectionString">Connection string for azure table storage account</param>
        public WindowsAzureTableStorageService(string StorageConnectionString)
        {
            storageConnectionString = StorageConnectionString;
        }

        public void createCloudTableClient()
        {
            if (tableClient == null)
            {
                // Retrieve the storage account from the connection string.
                CloudStorageAccount storageAccount = CloudStorageAccount.Parse(storageConnectionString);
                // Create the table client.
                tableClient = storageAccount.CreateCloudTableClient();
            }
        }

        public void closeCloudTableClient()
        {
            if (tableClient != null)
            {
                tableClient = null;
            }
        }

        /// <summary>
        /// Creates a table in Windows Azure Table Storage
        /// </summary>
        /// <param name="TableName">Name of the table to create</param>
        public void CreateTable(string TableName)
        {
        // Retrieve the storage account from the connection string.
            CloudStorageAccount storageAccount = CloudStorageAccount.Parse(storageConnectionString);

            // Create the table client.
            CloudTableClient tableClient = storageAccount.CreateCloudTableClient();

            // Create the table if it doesn't exist.
            CloudTable table = tableClient.GetTableReference(TableName);
            table = tableClient.GetTableReference(TableName);
            table.CreateIfNotExists();
            
        }
        /// <summary>
        /// Deletes a table in Windows Azure Table Storage
        /// </summary>
        /// <param name="TableName">Name of the table to delete</param>
        public void DeleteTable(string TableName)
        {
            // Create the table if it doesn't exist.
            CloudTable table = tableClient.GetTableReference(TableName);
            // Delete the table if deleteIfExists is true
            table.DeleteIfExists();
        }

        public void AddEntity(String TableName, ITableEntity Entity )
        {
            // Retrieve the storage account from the connection string.
            CloudStorageAccount storageAccount = CloudStorageAccount.Parse(storageConnectionString);

            // Create the table client.
            CloudTableClient tableClient = storageAccount.CreateCloudTableClient();

            // Create the CloudTable object that represents the "people" table.
            CloudTable table = tableClient.GetTableReference(TableName);

            // Create the TableOperation that inserts the customer entity.
            TableOperation insertOperation = TableOperation.Insert(Entity);

            // Execute the insert operation.
            table.Execute(insertOperation);
        }

        /// <summary>
        /// Adds batch of entities based on the following rules:
        /// 1. Batch maximum size is 100 records
        /// 2. Batch entitities must all share the same partition key
        /// Batches are put in through ASync tasks so that we can create as many HTTP calls as possible to improve performance.
        /// </summary>
        /// <param name="TableName">Name of the table to insert into</param>
        /// <param name="entities">List of entities to insert into the table</param>
        public async Task AddBatchAsync(String TableName, List<ITableEntity> Entities, int MaximumTaskCount = -1 )
        {
            batchTasks = new List<Task>();
            Debug.WriteLine("*** Start of AddBatch Async ***");
            
            // Retrieve the storage account from the connection string.
            CloudStorageAccount storageAccount = CloudStorageAccount.Parse(storageConnectionString);

            Debug.WriteLine("storageConnectionString = " + storageConnectionString);

            // Create the table client.
            CloudTableClient tableClient = storageAccount.CreateCloudTableClient();
                        
            // Create the CloudTable object that represents the "people" table.
            CloudTable table = tableClient.GetTableReference(TableName);

            Debug.WriteLine("Connected to table" + table.Name);

            List<TableBatchOperation> batchOperations = new List<TableBatchOperation>();

            // run a lync query to group list of entities by Partition Key
            var queryPartitionKey = from entity in Entities group entity by entity.PartitionKey into partitiongroup orderby partitiongroup.Key select partitiongroup; 

            foreach (var partitionGroup in queryPartitionKey)
            {
                string partitionKey = partitionGroup.Key;
                Debug.WriteLine("Starting work on partitionkey = " + partitionKey);
                int currentBatchRecords = 0;
                TableBatchOperation batch = new TableBatchOperation();
                foreach (ITableEntity entity in partitionGroup)
                {
                    if (currentBatchRecords < 100)
                    {
                        // currently less than the maximum number of records for a batch, so we can add to the current batch.
                        batch.Insert(entity);
                        currentBatchRecords++;
                    }
                    else
                    {
                        batchOperations.Add(batch);
                        batch = new TableBatchOperation();
                        currentBatchRecords = 0;
                        batch.Insert(entity);
                        currentBatchRecords++;
                    }
                }
                batchOperations.Add(batch);
            }

            Debug.WriteLine("Added batches = " + batchOperations.Count);

            // now we have collected all the batches, execute batches asyncronously
            // batchCount tracks how many batches have been processed
            // taskCount tracks the current number of tasks - when we hit the maximum number of tasks, we wait until they are all done and reset back to 0.  This avoids potential timeouts if asyncronous calls sit too long in queue.
            int batchCount = 0;
            int taskCount = 0;
            try {
                foreach (TableBatchOperation batch in batchOperations)
                {
                    batchCount++;
                    taskCount++;
                    Debug.WriteLine("Adding batch " + batchCount + " of " + batchOperations.Count);
                    Task<IList<TableResult>> task = table.ExecuteBatchAsync(batch);
                    batchTasks.Add(task);
                    if (MaximumTaskCount > 0 && taskCount >= MaximumTaskCount)
                    {
                        Debug.WriteLine("Maximum task threshold reached - waiting for existing tasks to finish.");
                        await Task.WhenAll(batchTasks);
                        taskCount = 0;
                    }
                }
                await Task.WhenAll(batchTasks);
            }
            catch (Exception e)
            {
                Debug.WriteLine(e.StackTrace);
                Debug.WriteLine(e.Message);
                throw e;
            }
        }

        /// <summary>
        /// Adds batch of entities based on the following rules:
        /// 1. Batch maximum size is 100 records
        /// 2. Batch entitities must all share the same partition key
        /// Batches are put through syncronously.
        /// </summary>
        /// <param name="TableName">Name of the table to insert into</param>
        /// <param name="entities">List of entities to insert into the table</param>
        public void AddBatch(String TableName, List<ITableEntity> entities)
        {
            // Retrieve the storage account from the connection string.
            CloudStorageAccount storageAccount = CloudStorageAccount.Parse(storageConnectionString);

            // Create the table client.
            CloudTableClient tableClient = storageAccount.CreateCloudTableClient();

            // Create the CloudTable object that represents the "people" table.
            CloudTable table = tableClient.GetTableReference(TableName);

            List<TableBatchOperation> batchOperations = new List<TableBatchOperation>();

            // run a lync query to group list of entities by Partition Key
            var queryPartitionKey = from entity in entities group entity by entity.PartitionKey into partitiongroup orderby partitiongroup.Key select partitiongroup;

            foreach (var partitionGroup in queryPartitionKey)
            {
                string partitionKey = partitionGroup.Key;
                int currentBatchRecords = 0;
                TableBatchOperation batch = new TableBatchOperation();
                foreach (ITableEntity entity in partitionGroup)
                {
                    if (currentBatchRecords < 100)
                    {
                        // currently less than the maximum number of records for a batch, so we can add to the current batch.
                        batch.Insert(entity);
                        currentBatchRecords++;
                    }
                    else
                    {
                        batchOperations.Add(batch);
                        batch = new TableBatchOperation();
                        currentBatchRecords = 0;
                        batch.Insert(entity);
                        currentBatchRecords++;
                    }
                }
                batchOperations.Add(batch);
            }
            // now we have collected all the batches, execute batches asyncronously
            foreach (TableBatchOperation batch in batchOperations)
            {
                table.ExecuteBatch(batch);
            }
        }

        /// <summary>
        /// Checks incoming partition keys for bad characters and strips them out
        /// </summary>
        /// <param name="PartitionKey"></param>
        /// <returns></returns>
        public static string createValidPartitionKey(string PartitionKey)
        {
            StringBuilder newPartitionKey = new StringBuilder();
            foreach (char i in PartitionKey.ToCharArray())
            {
                switch (i) {
                    case '/': { newPartitionKey.Append(' '); break; }
                    case '\\': { newPartitionKey.Append(' '); break; }
                    case '#': { newPartitionKey.Append(' '); break; }
                    case '?': { newPartitionKey.Append(' '); break; }
                    default:
                        {
                            newPartitionKey.Append(i);
                            break;
                        }
                }
                    
            }
            return newPartitionKey.ToString();
             
        }

    }
}
