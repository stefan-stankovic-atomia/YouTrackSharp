using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace YouTrackSharp.Issues
{
    /// <summary>
    /// A class that represents a REST API client for <a href="https://www.jetbrains.com/help/youtrack/standalone/Issues-Related-Methods.html">YouTrack Issues Related Methods</a>.
    /// It uses a <see cref="Connection" /> implementation to connect to the remote YouTrack server instance.
    /// </summary>
    public partial class IssuesService : IIssuesService
    {
        private readonly Connection _connection;
        
        private static readonly string[] ReservedFields = 
        {
            "id", "entityid", "jiraid", "summary", "description", "markdown"
        };

        /// <summary>
        /// Creates an instance of the <see cref="IssuesService" /> class.
        /// </summary>
        /// <param name="connection">A <see cref="Connection" /> instance that provides a connection to the remote YouTrack server instance.</param>
        public IssuesService(Connection connection)
        {
            _connection = connection ?? throw new ArgumentNullException(nameof(connection));
        }

        /// <inheritdoc />
        public async Task<Issue> GetIssue(string issueId, bool wikifyDescription = false)
        {
            if (string.IsNullOrEmpty(issueId))
            {
                throw new ArgumentNullException(nameof(issueId));
            }
            
            var client = await _connection.GetAuthenticatedHttpClient();
            var response = await client.GetAsync($"rest/issue/{issueId}?wikifyDescription={wikifyDescription}");

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return null;
            }

            response.EnsureSuccessStatusCode();
            
            return JsonConvert.DeserializeObject<Issue>(await response.Content.ReadAsStringAsync());
        }

        /// <inheritdoc />
        public async Task<bool> Exists(string issueId)
        {
            if (string.IsNullOrEmpty(issueId))
            {
                throw new ArgumentNullException(nameof(issueId));
            }
            
            var client = await _connection.GetAuthenticatedHttpClient();
            var response = await client.GetAsync($"rest/issue/{issueId}/exists");

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return false;
            }

            response.EnsureSuccessStatusCode();

            return true;
        }

        /// <inheritdoc />
        public async Task<string> CreateIssue(string projectId, Issue issue)
        {
            if (string.IsNullOrEmpty(projectId))
            {
                throw new ArgumentNullException(nameof(projectId));
            }
            
            var queryString = new List<string>(4);
            queryString.Add($"project={projectId}");
            
            if (!string.IsNullOrEmpty(issue.Summary))
            {
                queryString.Add($"summary={WebUtility.UrlEncode(issue.Summary)}");
            }
            if (!string.IsNullOrEmpty(issue.Description))
            {
                queryString.Add($"description={WebUtility.UrlEncode(issue.Description)}");
            }
            if (issue.IsMarkdown)
            {
                queryString.Add($"markdown=true");
            }
            
            var query = string.Join("&", queryString);
            
            var client = await _connection.GetAuthenticatedHttpClient();
            var response = await client.PutAsync($"rest/issue?{query}", new MultipartFormDataContent());

            response.EnsureSuccessStatusCode();
            
            // Extract issue id from Location header response
            const string marker = "rest/issue/";
            var locationHeader = response.Headers.Location.ToString();
            
            var issueId = locationHeader.Substring(locationHeader.IndexOf(marker, StringComparison.OrdinalIgnoreCase) + marker.Length);

            // For every custom field, apply a command
            var customFields = issue.Fields
                .Where(field => !ReservedFields.Contains(field.Name.ToLower()))
                .ToDictionary(field => field.Name, field => field.Value);
            
            foreach (var customField in customFields)
            {
                if (!(customField.Value is string) && customField.Value is System.Collections.IEnumerable enumerable)
                {
                    await ApplyCommand(issueId, $"{customField.Key} {string.Join(" ", enumerable.OfType<string>())}", string.Empty);
                }
                else if (customField.Value is DateTime dateTime)
                {
                    await ApplyCommand(issueId, $"{customField.Key} {dateTime:s}", string.Empty);
                }
                else if (customField.Value is DateTimeOffset dateTimeOffset)
                {
                    await ApplyCommand(issueId, $"{customField.Key} {dateTimeOffset:s}", string.Empty);
                }
                else
                {
                    await ApplyCommand(issueId, $"{customField.Key} {customField.Value}", string.Empty);
                }
            }
            
            // Add comments?
            foreach (var issueComment in issue.Comments)
            {
                await ApplyCommand(issueId, "comment", issueComment.Text, runAs: issueComment.Author);
            }
            
            // Add tags?
            foreach (var issueTag in issue.Tags)
            {
                await ApplyCommand(issueId, $"tag {issueTag.Value}");
            }
            
            return issueId;
        }

        /// <inheritdoc />
        public async Task UpdateIssue(string issueId, string summary = null, string description = null, bool? isMarkdown = null)
        {
            if (string.IsNullOrEmpty(issueId))
            {
                throw new ArgumentNullException(nameof(issueId));
            }

            if (summary == null && description == null)
            {
                return;
            }
            
            var queryString = new List<string>(3);
            if (!string.IsNullOrEmpty(summary))
            {
                queryString.Add($"summary={WebUtility.UrlEncode(summary)}");
            }
            if (!string.IsNullOrEmpty(description))
            {
                queryString.Add($"description={WebUtility.UrlEncode(description)}");
            }
            if (isMarkdown.HasValue)
            {
                queryString.Add($"markdown={isMarkdown.ToString().ToLowerInvariant()}");
            }
            
            var query = string.Join("&", queryString);

            var client = await _connection.GetAuthenticatedHttpClient();
            var response = await client.PostAsync($"rest/issue/{issueId}?{query}", new MultipartFormDataContent());

            response.EnsureSuccessStatusCode();
        }
        
        /// <inheritdoc />
        public async Task ApplyCommand(string issueId, string command, string comment = null, bool disableNotifications = false, string runAs = null)
        {
            if (string.IsNullOrEmpty(issueId))
            {
                throw new ArgumentNullException(nameof(issueId));
            }
            
            if (string.IsNullOrEmpty(command))
            {
                throw new ArgumentNullException(nameof(command));
            }
            
            var queryString = new List<string>(4);
            queryString.Add($"command={WebUtility.UrlEncode(command)}");
            
            if (!string.IsNullOrEmpty(comment))
            {
                queryString.Add($"comment={WebUtility.UrlEncode(comment)}");
            }
            if (disableNotifications)
            {
                queryString.Add("disableNotifications=true");
            }
            if (!string.IsNullOrEmpty(runAs))
            {
                queryString.Add($"runAs={runAs}");
            }
            
            var query = string.Join("&", queryString);
            
            var client = await _connection.GetAuthenticatedHttpClient();
            var response = await client.PostAsync($"rest/issue/{issueId}/execute?{query}", new MultipartFormDataContent());
            
            if (response.StatusCode == HttpStatusCode.BadRequest)
            {
                // Try reading the error message
                var responseJson = JObject.Parse(await response.Content.ReadAsStringAsync());
                if (responseJson["value"] != null)
                {
                    throw new YouTrackErrorException(responseJson["value"].Value<string>());
                }
                else
                {
                    throw new YouTrackErrorException(Strings.Exception_UnknownError);
                }
            }
            
            response.EnsureSuccessStatusCode();
        }
        
        /// <inheritdoc />
        public async Task DeleteIssue(string issueId)
        {
            if (string.IsNullOrEmpty(issueId))
            {
                throw new ArgumentNullException(nameof(issueId));
            }
            
            var client = await _connection.GetAuthenticatedHttpClient();
            var response = await client.DeleteAsync($"rest/issue/{issueId}");

            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                return;
            }

            response.EnsureSuccessStatusCode();
        }

        /// <inheritdoc />
        public async Task<IEnumerable<Link>> GetLinksForIssue(string issueId)
        {
            if (string.IsNullOrEmpty(issueId))
            {
                throw new ArgumentNullException(nameof(issueId));
            }

            var client = await _connection.GetAuthenticatedHttpClient();
            var response = await client.GetAsync($"rest/issue/{issueId}/link");

            response.EnsureSuccessStatusCode();
            
            return JsonConvert.DeserializeObject<IEnumerable<Link>>(await response.Content.ReadAsStringAsync());
        }
    }
}