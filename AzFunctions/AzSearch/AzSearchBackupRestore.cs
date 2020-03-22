using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Extensions.Logging;
using Microsoft.Azure.Search;
using Microsoft.Azure.Search.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

/**
 * Microsoft Disclaimer:
 * This code is open source and you are free to modify it to suit your needs. Use this code at your own discretion.  Microsoft will try to address issues arising out of this code on a best efforts basis.
 *
 * Microsoft assumes no responsibility whatsoever for any damages resulting from the use of this code in 'Production' environments or elsewhere !
 */

namespace AzSearch
{
	public static class AzSearchBackupRestore
	{
		private static string SourceSearchServiceName; 
		private static string SourceAPIKey;
		private static string TargetSearchServiceName;
		private static string TargetAPIKey;

		private static SearchServiceClient SourceSearchClient;
		private static ISearchIndexClient SourceIndexClient;
		private static SearchServiceClient TargetSearchClient;
		private static ISearchIndexClient TargetIndexClient;

		private static List<string> fnames;
		private static ILogger logger;

		private static int MaxBatchSize = 500;          // JSON files will contain this many documents / file and can be up to 1000
		private static int ParallelizedJobs = 10;       // Output content in parallel jobs

        	[FunctionName("AzSyncCognitiveSearchKb")]
        	public static async Task Run([TimerTrigger("0 30 8 * * 1-5")]TimerInfo myTimer, ILogger log)
		{
            		log.LogInformation($"Func:AzSyncCognitiveSeaarchKb: Timer triggered, Begin: {DateTime.Now}");
			logger = log;

			SourceSearchServiceName = Environment.GetEnvironmentVariable("SrcSearchSvcName");
			TargetSearchServiceName = Environment.GetEnvironmentVariable("TgtSearchSvcName");
			SourceAPIKey = Environment.GetEnvironmentVariable("SrcSearchApiKey");
			TargetAPIKey = Environment.GetEnvironmentVariable("TgtSearchApiKey");

			SourceSearchClient = new SearchServiceClient(SourceSearchServiceName, new SearchCredentials(SourceAPIKey));
			TargetSearchClient = new SearchServiceClient(TargetSearchServiceName, new SearchCredentials(TargetAPIKey));

			// Get all the indexes from source
			IEnumerable<string> indexNames = await SourceSearchClient.Indexes.ListNamesAsync();

			// Re-Creating the synonym map
			await ReCreateSynonymMapAsync();

			// For each index in source do the following
			foreach (var nameOfIndex in indexNames)
			{
				fnames = new List<string>();

				SourceIndexClient = SourceSearchClient.Indexes.GetClient(nameOfIndex);
				TargetIndexClient = TargetSearchClient.Indexes.GetClient(nameOfIndex);

				// Extract the index schema and write to file
				log.LogInformation($"Writing Index Schema to {nameOfIndex}.schema\r\n");
				var indexSchema = await GetIndexSchemaAsync(nameOfIndex);

				// Extract the content to JSON files 
				int SourceDocCount = GetCurrentDocCount(SourceIndexClient);
				LaunchParallelDataExtraction(SourceDocCount, nameOfIndex);     // Output content from index to json files

				// Re-create and import content to target index
				await DeleteIndexAsync(nameOfIndex);
				await CreateTargetIndexAsync(indexSchema);
				ImportFromJSON(nameOfIndex);
				log.LogInformation("\r\nWaiting 10 seconds for target to index content...");
				log.LogInformation("NOTE: For really large indexes it may take longer to index all content.\r\n");
				Thread.Sleep(10000);

				// Validate all content is in target index
				int TargetDocCount = GetCurrentDocCount(TargetIndexClient);
				log.LogInformation($"Source Index {nameOfIndex} contains {SourceDocCount} docs");
				log.LogInformation($"Target Index {nameOfIndex} contains {TargetDocCount} docs\r\n");

			}

			// ID03192020.so
			// Console.WriteLine("Press any key to continue...");
			// Console.ReadLine();
			// ID03192020.eo
            		log.LogInformation($"Func:AzSyncCognitiveSeaarchKb: End: {DateTime.Now}");
		}

		static async Task ReCreateSynonymMapAsync()
		{
			// QnAMaker uses the synonymmap of synonym-map
			string synonymMapName = "synonym-map";
			SynonymMap synonymMap = null;

			// Get the Synonym map from the source;
			try
			{
				synonymMap = await SourceSearchClient.SynonymMaps.GetAsync(synonymMapName);
			}
			catch (Exception ex)
			{
				Console.WriteLine("Error: {0}", ex.Message.ToString());
			}

			// Delete the existing synonymmap on the target
			try
			{
				await TargetSearchClient.SynonymMaps.DeleteAsync(synonymMapName);
			}
			catch (Exception ex)
			{
				Console.WriteLine("Error: {0}", ex.Message.ToString());
			}

			// Create the new synonym in the target if it exists
			if (!(synonymMap is null))
			{
				try
				{
					await TargetSearchClient.SynonymMaps.CreateOrUpdateAsync(synonymMap);
				}
				catch (Exception ex)
				{
					Console.WriteLine("Error: {0}", ex.Message.ToString());
				}
			}

			Console.WriteLine("Synonym Map created in Target");
		}

		static void LaunchParallelDataExtraction(int CurrentDocCount, string nameOfIndex)
		{
			// Launch output in parallel
			string IDFieldName = GetIDFieldName(nameOfIndex);
			int FileCounter = 0;
			for (int batch = 0; batch <= (CurrentDocCount / MaxBatchSize); batch += ParallelizedJobs)
			{

				List<Task> tasks = new List<Task>();
				for (int job = 0; job < ParallelizedJobs; job++)
				{
					FileCounter++;
					int fileCounter = FileCounter;
					if ((fileCounter - 1) * MaxBatchSize < CurrentDocCount)
					{
						string fn = nameOfIndex + "-" + DateTime.Now.Ticks.ToString("x") + ".json";
						logger.LogInformation($"Writing {MaxBatchSize} docs to {fn}");

						/** 
						tasks.Add(Task.Factory.StartNew(() =>
							ExportToJSON((fileCounter - 1) * MaxBatchSize, IDFieldName, nameOfIndex + fileCounter + ".json")
						)); */
						tasks.Add(Task.Factory.StartNew(() =>
							ExportToJSON((fileCounter - 1) * MaxBatchSize, IDFieldName, fn)
						));
					}

				}
				Task.WaitAll(tasks.ToArray());  // Wait for all the stored procs in the group to complete
			}

			return;
		}

		static void ExportToJSON(int Skip, string IDFieldName, string FileName)
		{
			// Extract all the documents from the selected index to JSON files in batches of 500 docs / file
			fnames.Add(FileName);
			string json = string.Empty;
			try
			{
				SearchParameters sp = new SearchParameters()
				{
					SearchMode = SearchMode.All,
					Top = MaxBatchSize,
					Skip = Skip
				};
				DocumentSearchResult<Document> response = SourceIndexClient.Documents.Search("*", sp);

				foreach (var doc in response.Results)
				{
					json += JsonConvert.SerializeObject(doc.Document) + ",";
					// Geospatial is formatted such that it needs to be changed for reupload
					// Unfortunately since it comes down in Lat, Lon format, I need to alter it to Lon, Lat for upload

					//TODO is this still needed? I didn't see this in my QnA Maker export
					while (json.IndexOf("CoordinateSystem") > -1)
					{
						// At this point the data looks like this
						// {"Latitude":38.3399,"Longitude":-86.0887,"IsEmpty":false,"Z":null,"M":null,"CoordinateSystem":{"EpsgId":4326,"Id":"4326","Name":"WGS84"}}
						int LatStartLocation = json.IndexOf("\"Latitude\":");
						LatStartLocation = json.IndexOf(":", LatStartLocation) + 1;
						int LatEndLocation = json.IndexOf(",", LatStartLocation);
						int LonStartLocation = json.IndexOf("\"Longitude\":");
						LonStartLocation = json.IndexOf(":", LonStartLocation) + 1;
						int LonEndLocation = json.IndexOf(",", LonStartLocation);
						string Lat = json.Substring(LatStartLocation, LatEndLocation - LatStartLocation);
						string Lon = json.Substring(LonStartLocation, LonEndLocation - LonStartLocation);

						// Now it needs to look like this
						// { "type": "Point", "coordinates": [-122.131577, 47.678581] }
						int GeoStartPosition = json.IndexOf("\"Latitude\":") - 1;
						int GeoEndPosition = json.IndexOf("}}", GeoStartPosition) + 2;
						string updatedJson = json.Substring(0, GeoStartPosition) + "{ \"type\": \"Point\", \"coordinates\": [";
						updatedJson += Lon + ", " + Lat + "] }";
						updatedJson += json.Substring(GeoEndPosition);
						json = updatedJson;
					}

					json = json.Replace("\"Latitude\":", "\"type\": \"Point\", \"coordinates\": [");
					json = json.Replace("\"Longitude\":", "");
					json = json.Replace(",\"IsEmpty\":false,\"Z\":null,\"M\":null,\"CoordinateSystem\":{\"EpsgId\":4326,\"Id\":\"4326\",\"Name\":\"WGS84\"}", "]");
					json += "\r\n";

					//{ "type": "Point", "coordinates": [-122.131577, 47.678581] }
					//{"Latitude":41.113,"Longitude":-95.6269}
					//json += "\r\n";

				}

				// Output the formatted content to a file
				json = json.Substring(0, json.Length - 3); // remove trailing comma
				WriteToStorageBlob("{\"value\": [" + json + "]}",FileName);
				// File.WriteAllText(FileName, "{\"value\": [");
				// File.AppendAllText(FileName, json);
				// File.AppendAllText(FileName, "]}");
				string recCount = response.Results.Count.ToString();
				logger.LogInformation($"ExportToJSON: Total documents written: {recCount}");
				json = string.Empty;


			}
			catch (Exception ex)
			{
				Console.WriteLine("Error: {0}", ex.Message.ToString());
			}
			return;
		}

		static void WriteToStorageBlob(string idxContent, string fname) {
			string conString = Environment.GetEnvironmentVariable("AZURE_STORAGE_CONNECTION_STRING");
			BlobServiceClient blobServiceClient = new BlobServiceClient(conString);
			string containerName = Environment.GetEnvironmentVariable("AzStorageContainerName");
			BlobContainerClient containerClient = blobServiceClient.GetBlobContainerClient(containerName);

			BlobClient blobClient = containerClient.GetBlobClient(fname);
			
			using ( var stream = GenerateStreamFromString(idxContent) ) {
				blobClient.Upload(stream,true);
			};
			logger.LogInformation($"WriteToStorageBlob: Wrote source index data to Az Storage: {fname}");
		}

		static Stream GenerateStreamFromString(string s)
		{
		    var stream = new MemoryStream();
		    var writer = new StreamWriter(stream);
		    writer.Write(s);
		    writer.Flush();
		    stream.Position = 0;
		    return stream;
		}

		static string GetIDFieldName(string nameOfIndex)
		{
			// Find the id field of this index
			string IDFieldName = string.Empty;
			try
			{
				var schema = SourceSearchClient.Indexes.Get(nameOfIndex);
				foreach (var field in schema.Fields)
				{
					if (field.IsKey ?? false)
					{
						IDFieldName = Convert.ToString(field.Name);
						break;
					}
				}

			}
			catch (Exception ex)
			{
				Console.WriteLine("Error: {0}", ex.Message.ToString());
			}
			return IDFieldName;
		}
		
		static async Task<Microsoft.Azure.Search.Models.Index> GetIndexSchemaAsync(string nameOfIndex)
		{
			// Extract the schema for this index

			Microsoft.Azure.Search.Models.Index indexSchema = null;

			try
			{
				indexSchema = await SourceSearchClient.Indexes.GetAsync(nameOfIndex);
			}
			catch (Exception ex)
			{
				Console.WriteLine("Error: {0}", ex.Message.ToString());
			}

			return indexSchema;
		}

		private static async Task<bool> DeleteIndexAsync(string nameOfIndex)
		{
			// Delete the index if it exists
			try
			{
				await TargetSearchClient.Indexes.DeleteAsync(nameOfIndex);
			}
			catch (Exception ex)
			{
				Console.WriteLine("Error deleting index: {0}\r\n", ex.Message);
				Console.WriteLine("Did you remember to set your SearchServiceName and SearchServiceApiKey?\r\n");
				return false;
			}

			return true;
		}

		static async Task CreateTargetIndexAsync(Microsoft.Azure.Search.Models.Index indexSchema)
		{
			try
			{
				await TargetSearchClient.Indexes.CreateOrUpdateAsync(indexSchema);
			}
			catch (Exception ex)
			{
				Console.WriteLine("Error: {0}", ex.Message.ToString());
			}
		}



		static int GetCurrentDocCount(ISearchIndexClient IndexClient)
		{
			// Get the current doc count of the specified index
			try
			{
				SearchParameters sp = new SearchParameters()
				{
					SearchMode = SearchMode.All,
					IncludeTotalResultCount = true
				};

				DocumentSearchResult<Document> response = IndexClient.Documents.Search("*", sp);
				return Convert.ToInt32(response.Count);

			}
			catch (Exception ex)
			{
				Console.WriteLine("Error: {0}", ex.Message.ToString());
			}

			return -1;

		}

		static void ImportFromJSON(string nameOfIndex)
		{
			// Take JSON file and import this as-is to target index
			Uri ServiceUri = new Uri("https://" + TargetSearchServiceName + ".search.windows.net");
			HttpClient HttpClient = new HttpClient();
			HttpClient.DefaultRequestHeaders.Add("api-key", TargetAPIKey);

			try
			{
				foreach (string fileName in fnames) 
				// foreach (string fileName in Directory.GetFiles(Directory.GetCurrentDirectory(), nameOfIndex + "*.json"))
				{
					logger.LogInformation($"ImportFromJSON: Uploading documents from file {fileName}");
					// string json = File.ReadAllText(fileName);
					string json = ReadFromStorageBlob(fileName);
					Uri uri = new Uri(ServiceUri, "/indexes/" + nameOfIndex + "/docs/index");
					HttpResponseMessage response = AzureSearchHelper.SendSearchRequest(HttpClient, HttpMethod.Post, uri, json);
					response.EnsureSuccessStatusCode();

					//TODO use TargetIndexClient.Documents.IndexAsync(...)
				}
			}
			catch (Exception ex)
			{
				Console.WriteLine("Error: {0}", ex.Message.ToString());
			}
		}

		static string ReadFromStorageBlob(string fname) {
			string conString = Environment.GetEnvironmentVariable("AZURE_STORAGE_CONNECTION_STRING");
			BlobServiceClient blobServiceClient = new BlobServiceClient(conString);
			string containerName = Environment.GetEnvironmentVariable("AzStorageContainerName");
			BlobContainerClient containerClient = blobServiceClient.GetBlobContainerClient(containerName);

			BlobClient blobClient = containerClient.GetBlobClient(fname);
			
			// Download the blob content.
			BlobDownloadInfo download = blobClient.Download();
			StreamReader reader = new StreamReader( download.Content );

			logger.LogInformation($"ReadFromStorageBlob: Read source index data from Az Storage: {fname}");
			return( reader.ReadToEnd() );
		}
	}
}
