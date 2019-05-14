# QnAMakerBackupRestore
Backup and restore your Azure search index which is part of the QnAMaker runtime

Since Azure search does not have an in-place SKU upgrade, to upgrade your QnAMaker stack you can use this code to restore your knowledge base data to the upgraded Azure search SKU.

See [here](https://aka.ms/qnamaker-docs-qnamaker-upgrade) for more details.

# Basic usage

Open the solution '\AzureSearchBackupRestore\QnAMakerAzureSearchBackupRestore.sln' in VS2017
Change the source and target in \AzureSearchBackupRestore\Program.cs to your source and target search service's
Run the program from VS2017


## Example change

```C#
private static string SourceSearchServiceName = "searchservicewestus-asnb6mhgpkm25xc";
private static string SourceAPIKey = "<32 hex chars>";
private static string TargetSearchServiceName = "searchserviceeastus-xnnb3mhgpkm77gb";
private static string TargetAPIKey = "<32 hex chars>";
```

Source and Target API Key refer to the Search Service keys in the Azure portal

The SourceSearchServiceName and TargetSearchServiceName refer to Search Service URL minus the protocol and .search.windows.net. The above example Source would be represented by https://searchservicewestus-asnb6mhgpkm25xc.search.windows.net
 
