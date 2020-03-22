# Deploy the Azure Search Backup and Restore application in an Azure Function

This project describes the steps for deploying the backup and restore application within an Azure Function.  The Function is configured with a **Timer** input trigger which can be configured to periodically synchronize the knowledge base documents across the **Source** and **Target** Azure Search instances. 

Follow the steps below for building and running the Function application on Azure.

1. (Optional) Fork this [GitHub repository](https://github.com/pchoudhari/QnAMakerBackupRestore).

   If you intend to modify the Function code before deploying it to Azure, you will need to Fork this repository into your GitHub account.  Using a browser, login to your GitHub account and **Fork** this repository.

2. Open an Azure Cloud Shell session.

   We will clone this repository, build and deploy the Azure Function on Azure via the Azure Cloud Shell. Using a web brower, access the [Azure Cloud Shell] (https://shell.azure.com/) and open a Linux **Bash** session.

3. Create a Function Application in Azure.

   Refer to the [Azure Function documentation](https://docs.microsoft.com/en-us/azure/azure-functions/functions-create-first-azure-function-azure-cli?tabs=bash%2Cbrowser&pivots=programming-language-csharp#create-supporting-azure-resources-for-your-function) to create an application.  Alternatively, if you are familiar with Azure Portal, you can also create the Function application via the Portal. 

4. Update the Azure Function configuration parameters.

   Using a web browser, access the Azure Function application which you just created. Access the **Configuration** panel and create the application parameters with corresponding values as per the table below.

   Function Parameter Name | Description
   ----------------------- | -----------
   AZURE_STORAGE_CONNECTION_STRING | Specify the **Connection String** to an Azure Storage account.
   AzStorageContainerName | Specify a storage **Container** name in the Azure Storage account specified by the parameter AZURE_STORAGE_CONNECTION_STRING.  This container will be used to backup the knowledge base documents from the source Azure Search instance and restore it in the target instance.
   SrcSearchSvcName | Specify the source Azure Search instance **Service Name**.
   TgtSearchSvcName | Specify the target Azure Search instance **Service Name**.
   SrcSearchApiKey | Specify the source Azure Search instance **API Key**.
   TgtSearchApiKey | Specify the source Azure Search instance **API Key**.

5. Clone this GitHub repository](https://github.com/pchoudhari/QnAMakerBackupRestore).

   In the Azure cloud shell, clone this GitHub repository. Refer to the command snippet below.

   ```bash
   # Create the root directory to store all your projects.
   $ mkdir git-repos
   #
   # Switch current directory to 'git-repos'
   $ cd git-repos
   #
   # Clone the master or your GitHub repository. Cloning from your GitHub account 
   # will allow you to make changes to the application artifacts.
   # Substitute your GitHub Account ID in the URL as below.
   $ git clone https://github.com/<YOUR-GITHUB-ACCOUNT/QnAMakerBackupRestore.git
   #
   ```
 
6. Deploy the Azure Search Synchronization Function on Azure.

   Refer to the commands in the snippet below to deploy the Function Application on Azure.

   ```bash
   # Use Azure Function Core Tools to deploy the Function on Azure.
   # Substitute the correct value for the Function Application name.
   #
   $ func azure functionapp publish <name-of-function-application>
   #
   ```
