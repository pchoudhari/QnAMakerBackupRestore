//Copyright 2014 Microsoft

//Licensed under the Apache License, Version 2.0 (the "License");
//you may not use this file except in compliance with the License.
//You may obtain a copy of the License at

//       http://www.apache.org/licenses/LICENSE-2.0

//Unless required by applicable law or agreed to in writing, software
//distributed under the License is distributed on an "AS IS" BASIS,
//WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
//See the License for the specific language governing permissions and
//limitations under the License.

using System;
using System.Net.Http;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Serialization;

namespace AzSearch
{
	public class AzureSearchHelper
	{
		public const string ApiVersionString = "api-version=2017-11-11";

		public static HttpResponseMessage SendSearchRequest(HttpClient client, HttpMethod method, Uri uri, string json = null, string odata = null)
		{
			UriBuilder builder = new UriBuilder(uri);
			string separator = string.IsNullOrWhiteSpace(builder.Query) ? string.Empty : "&";
			builder.Query = builder.Query.TrimStart('?') + separator + ApiVersionString;

			if (odata != null)
			{
				builder.Query = builder.Query.TrimStart('?') + "&" + odata;
			}

			var request = new HttpRequestMessage(method, builder.Uri);

			if (json != null)
			{
				request.Content = new StringContent(json, Encoding.UTF8, "application/json");
			}

			return client.SendAsync(request).Result;
		}
	}
}
