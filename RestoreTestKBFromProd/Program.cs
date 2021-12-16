namespace RestoreTestKbFromProd
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Net.Http;
    using System.Net.Http.Headers;
    using System.Threading.Tasks;
    using Microsoft.Azure.CognitiveServices.Knowledge.QnAMaker;
    using Microsoft.Azure.CognitiveServices.Knowledge.QnAMaker.Models;
    using Newtonsoft.Json;
    using Azure.Search.Documents.Indexes;

    public class Program
    {
        // Example: https://demoqnaexternalasecs4.cognitiveservices.azure.com/
        private static string cognitiveServiceEndpoint = "QNA_MAKER_ENDPOINT";

        // Example: <secret key>
        private static string cogntiveServiceKey = "QNA_MAKER_SUBSCRIPTION_KEY";

        // Example: English
        // provide the KB language for your QnA Service. That is used to set testKB index analyzer. Not required for multiple language resource.
        private static string testKBIndexLanguage = "QNA_MAKER_TESTKBINDEX_LANGUAGE";

        // Example: true/ false
        // Applicable for QnA Maker V2/ Language service. This is used to decide if each published kb has a separate test index.
        private static bool enableMultipleLanguages = false;

        // Example: https://searchservice.search.windows.net
        // The search service associated with the QnA Maker/ Language resource.
        private static string searchServiceEndpoint = "SEARCH_SERVICE_ENDPOINT";

        // Example: <secret key>
        // Key of the search service.
        private static string searchServiceApiKey = "SEARCH_SERVICE_API_KEY";

        private IQnAMakerClient client;

        private SearchIndexClient searchClient;

        public static void Main(string[] args)
        {
            Console.WriteLine("Started Processing..\n");
            var program = new Program();
            program.Process().Wait();
            Console.WriteLine("\nCompleted.");
        }

        private async Task Process()
        {
            client = new QnAMakerClient(new ApiKeyServiceClientCredentials(cogntiveServiceKey))
            {
                Endpoint = cognitiveServiceEndpoint
            };

            if (!searchServiceApiKey.Equals("SEARCH_SERVICE_API_KEY"))
            {
                searchClient = new SearchIndexClient(new Uri(searchServiceEndpoint), new Azure.AzureKeyCredential(searchServiceApiKey));
            }

            var allKbs = await this.GetKbs();
            if (allKbs.Count <= 0)
            {
                Console.WriteLine("There are no KBs in this service");
            }
            else
            {
                var publishedKbs = allKbs.Where(kb => !string.IsNullOrEmpty(kb.LastPublishedTimestamp)).ToList();
                if (publishedKbs.Count > 0)
                {
                    if (enableMultipleLanguages)
                    {
                        Console.WriteLine($"Total KB Count: {allKbs.Count}, published Kbs Count: {publishedKbs.Count}, unpublished Kbs: {allKbs.Count - publishedKbs.Count}");
                        foreach (var publishedKb in publishedKbs)
                        {
                            try
                            {
                                var testIndexName = publishedKb.Id + "testkb";
                                var exists = await searchClient.GetIndexAsync(testIndexName);
                            }
                            catch (Azure.RequestFailedException e) when (e.Status == 404)
                            {
                                await this.RestoreTestKbForMultipleLanguages(publishedKb.Id);
                            }
                        }

                        return;
                    }

                    Console.WriteLine($"Total KB Count: {allKbs.Count}, published Kbs Count: {publishedKbs.Count}, unpublished Kbs (can't be restored): {allKbs.Count - publishedKbs.Count}");
                    foreach (var publishedKb in publishedKbs)
                    {
                        Console.WriteLine($"Existing Published KBId: {publishedKb.Id} with Language: {publishedKb.Language}");
                    }

                    if (testKBIndexLanguage == "QNA_MAKER_TESTKBINDEX_LANGUAGE")
                    {
                        Console.ForegroundColor = ConsoleColor.Red;
                        Console.WriteLine("\n\nPlease set the value for variable 'testKBIndexLanguage'. It should be mostly same as above published KB language");
                        return;
                    }

                    // create a sample KB and delete - to create testKb index in the Azure Search
                    // create azure search testkb index analyzer with the same language as first published kb
                    var sampleKbId = await this.CreateSampleKb(testKBIndexLanguage);
                    await this.DeleteKB(sampleKbId);

                    foreach (var publishedKb in publishedKbs)
                    {
                        await this.RestoreTestKbFromProd(publishedKb.Id);
                    }
                }
            }
        }

        private async Task<IList<KnowledgebaseDTO2>> GetKbs()
        {
            // default SDK Kblist don't have 'language' param. so, getting raw output and custom deserialize
            // var knowledgebases = await this.client.Knowledgebase.ListAllAsync();

            var url = cognitiveServiceEndpoint + "/qnamaker/v5.0-preview.1/knowledgebases";
            using var client = new HttpClient();
            client.DefaultRequestHeaders.Add("Ocp-Apim-Subscription-Key", cogntiveServiceKey);
            client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

            var response = await client.GetStringAsync(url);

            var kbs = JsonConvert.DeserializeObject<KnowledgebasesDTO2>(response);

            return kbs.Knowledgebases;
        }

        private async Task RestoreTestKbFromProd(string kbId)
        {
            Console.WriteLine($"Started Restoring Kb: {kbId}");
            var kbData = await client.Knowledgebase.DownloadAsync(kbId, EnvironmentType.Prod);


            var replaceKbDTO = new ReplaceKbDTO();
            replaceKbDTO.QnAList = kbData.QnaDocuments;

            await client.Knowledgebase.ReplaceAsync(kbId, replaceKbDTO);
            Console.WriteLine($"Completed Restoring Kb: {kbId}");
        }

        private async Task<string> CreateSampleKb(string language)
        {
            Console.WriteLine($"\nCreating SampleKB to create default azure search 'testkb' index for language: {language}");
            var qna1 = new QnADTO
            {
                Answer = "a1",
                Questions = new List<string> { "q1" }

            };

            var createKbDto = new CreateKbDTO
            {
                Name = "Sample KB to create TestKbIndex In Azure Search",
                QnaList = new List<QnADTO> { qna1 },
                Language = language

            };

            var createOp = await this.client.Knowledgebase.CreateAsync(createKbDto);
            createOp = await this.MonitorOperation(createOp);

            return createOp.ResourceLocation.Replace("/knowledgebases/", string.Empty);
        }

        private async Task DeleteKB(string kbId)
        {
            Console.WriteLine($"Deleting the sampleKB created.\n");
            await this.client.Knowledgebase.DeleteAsync(kbId);
        }

        // <MonitorOperation>
        private async Task<Operation> MonitorOperation(Operation operation)
        {
            // Loop while operation is success
            for (int i = 0;
                i < 20 && (operation.OperationState == OperationStateType.NotStarted || operation.OperationState == OperationStateType.Running);
                i++)
            {
                Console.WriteLine("\tWaiting for operation: {0} to complete.", operation.OperationId);
                await Task.Delay(5000);
                operation = await this.client.Operations.GetDetailsAsync(operation.OperationId);
            }

            if (operation.OperationState != OperationStateType.Succeeded)
            {
                throw new Exception($"\tOperation {operation.OperationId} failed to completed.");
            }

            return operation;
        }
        // </MonitorOperation>

        private async Task RestoreTestKbForMultipleLanguages(string kbId)
        {
            // We need to add a dummy QnA to create the relevant test index for the KB. In subsequent steps we'll override this with actual Prod KB content.
            var qna = new QnADTO() { Answer = "dummy answer", Questions = new List<string> { "dummy question" } };
            var updateQnA = new UpdateKbOperationDTO();
            updateQnA.Add = new UpdateKbOperationDTOAdd();
            updateQnA.Add.QnaList = new List<QnADTO>() { qna };

            // creates <kbid>testkb
            var operation = await client.Knowledgebase.UpdateAsync(kbId, updateQnA);
            await this.MonitorOperation(operation);
            await this.RestoreTestKbFromProd(kbId);
        }
    }

    class KnowledgebasesDTO2
    {
        public List<KnowledgebaseDTO2> Knowledgebases { get; set; }
    }

    class KnowledgebaseDTO2 : KnowledgebaseDTO
    {
        public string Language { get; set; }
    }
}