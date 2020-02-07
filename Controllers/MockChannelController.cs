using Microsoft.Bot.Connector;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using System.Web.Http;
using System.Web.Http.Description;

namespace MockChannel.Controllers
{
    [RoutePrefix("v3/conversations")]
    public class MockChannelController : ApiController
    {
        static readonly HttpClient _botClient = new HttpClient();
        static readonly HttpClient _tokenClient = new HttpClient();
        static TokenResponse _tokenResponse;
        static Dictionary<long, MockTurn> _turns = new Dictionary<long, MockTurn>();
        static private readonly object _conversationLock = new object();
        static long _conversationCount;
        static long _activityCount;
        static long _totalResponseTime;
        static Stopwatch _startOfTest = new Stopwatch();
        static double _fastestResponseTime = double.MaxValue;
        static double _slowestResponseTime;
        static double _messagePerSecond;
        string _appID;
        string _appPassword;
        string _botEndpoint;
        
        public MockChannelController()
        {
            _appID = ConfigurationManager.AppSettings["AppId"];
            _appPassword = ConfigurationManager.AppSettings["AppPassword"];
            _botEndpoint = ConfigurationManager.AppSettings["BotEndpoint"];
        }

        /// <summary>
        /// CreateConversation
        /// </summary>
        /// <remarks>
        /// Markdown=Content\Methods\CreateConversation.md
        /// </remarks>
        /// <param name="parameters">Parameters to create the conversation from</param>
        [HttpPost]
        [Route("")]  // Matches 'v3/conversations/'
        public HttpResponseMessage CreateConversation([FromBody]ConversationParameters parameters)
        {
            lock (_conversationLock)
            {
                _conversationCount++;
            }

            Uri _serviceUrl = new Uri(Request.RequestUri, "/");

            return Request.CreateResponse(HttpStatusCode.Created, new ConversationResourceResponse(id: _conversationCount.ToString(), serviceUrl: _serviceUrl.ToString()));
        }

        /// <summary>
        /// Sends an activity to the bot's endpoint just like a client like Web Chat or the Bot Emulator does
        /// </summary>
        /// <param name="conversationId">Conversation ID</param>
        /// <param name="activity">Activity to send</param>
        /// <remarks>
        /// This method mimics bot clients like web chat or the Bot emulator and sends an activity directly to
        /// the bot's endpoint.  It is not part of the mock channel but since its an API controller it was easy
        /// and convient to add this API to this project.
        /// 
        /// A full implementation of this method will manage starting conversation, continuing existing conversation,
        /// and ending completed conversation.  Right now it just starts a new conversation for every incoming 
        /// activity since we don't have any multi-turn conversation is this load test but later it should handle
        /// all the key scenarios.
        /// 
        /// Note: For information on how Attribute Routing works, see: https://docs.microsoft.com/en-us/aspnet/core/mvc/controllers/routing?view=aspnetcore-3.1#attribute-routing
        /// </remarks>
        [HttpPost]
        [Route("{conversationId}/activities")]  // Matches 'v3/conversations/{conversationId}/activities'
        async public Task<HttpResponseMessage> SendToConversation(string conversationId, [FromBody] Activity activity)
        {
            MockTurn turn;
            HttpResponseMessage response;
            var requestBody = new Dictionary<string, string>();

            requestBody.Add("grant_type", "client_credentials");
            requestBody.Add("client_id", _appID);
            requestBody.Add("client_secret", _appPassword);
            requestBody.Add("scope", $"{_appID}/.default");

            // Get BearerToken if we haven't yet
            if (_tokenResponse == null)
            {
                using (var bearerContent = new FormUrlEncodedContent(requestBody))
                {
                    if (!_tokenClient.DefaultRequestHeaders.Contains("Host"))
                        _tokenClient.DefaultRequestHeaders.Add("Host", "login.microsoftonline.com");

                    // Send the request
                    response = await _tokenClient.PostAsync(new Uri("https://login.microsoftonline.com/botframework.com/oauth2/v2.0/token"), bearerContent);

                    _tokenResponse = JsonConvert.DeserializeObject<TokenResponse>(await response.Content.ReadAsStringAsync());
                }
            }

            // Add incoming Activity to Turn database
            lock (_conversationLock)
            {
                if (_activityCount == 0)
                {
                    // Start timer for test so we can calculate message per second on summary
                    _startOfTest.Start();
                }

                // Get new activity ID
                activity.Id = (++_activityCount).ToString();

                turn = new MockTurn() { Activity = activity };

                _turns.Add(long.Parse(activity.Id), turn);
            }

            // Send Activity to bot's endpoint
            using (StringContent activityContent = new StringContent(JsonConvert.SerializeObject(activity), Encoding.UTF8, "application/json"))
            {
                if (!_botClient.DefaultRequestHeaders.Contains("Authorization"))
                    _botClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {_tokenResponse.access_token}");

                System.Diagnostics.Debug.WriteLine($"MockChannel SendActivity - activity.Conversation.Id - {activity.Conversation.Id}, activity.Id - {activity.Id}, activity.ServiceUrl - {activity.ServiceUrl} activity.GetActivityType - {activity.GetActivityType()}, activity.Text - {activity.Text}");

                // Start the Turn timer
                turn.TurnStart.Start();

                // Send the request
                response = await _botClient.PostAsync(new Uri(_botEndpoint), activityContent);
            }

            return Request.CreateResponse(response.StatusCode, new ResourceResponse(id: activity.Id));
        }

        /// <summary>
        /// ReplyToActivity
        /// </summary>
        /// <remarks>
        /// Markdown=Content\Methods\ReplyToActivity.md
        /// </remarks>
        /// <param name="activity">Activity to send</param>
        /// <param name="conversationId">Conversation ID</param>
        /// <param name="activityId">activityId the reply is to (OPTIONAL)</param>
        [HttpPost]
        [Route("{conversationId}/activities/{activityId}")]  // Matches 'v3/conversations/{conversationId}/activities/{activityId}'
        public HttpResponseMessage ReplyToActivity(string conversationId, string activityId, [FromBody]Activity activity)
        {
            // When a bot wants to reply to a user, it calls SendActivityAsync() which then calls this method so
            // that the channel (a mock channel in this case) can reply to the client in whatever manor the channel
            // requires.

            // TODO: Add whatever code is necessary to send the bot's response back to the client or do nothing here
            // if you just want to flow messages to your bot and you don't care about getting the response back to
            // the client.
            // You could create a custom API on this MockChannel and call it here and pass it the Activity and then
            // use the conversationId and activityId to correlate the bot response with its corresponding utterance
            MockTurn turn = _turns[long.Parse(activityId)];

            turn.TurnStart.Stop();

            _messagePerSecond = _activityCount / (_startOfTest.ElapsedMilliseconds / 1000.0);

            if (turn.TurnStart.ElapsedMilliseconds > _slowestResponseTime)
                _slowestResponseTime = turn.TurnStart.ElapsedMilliseconds;

            if (turn.TurnStart.ElapsedMilliseconds < _fastestResponseTime)
                _fastestResponseTime = turn.TurnStart.ElapsedMilliseconds;

            _totalResponseTime += turn.TurnStart.ElapsedMilliseconds;

            lock(_conversationLock)
            {
                _turns.Remove(long.Parse(activityId));
            }

            System.Diagnostics.Debug.WriteLine($"MockChannel ReplyToActivity - conversationId - {conversationId}, activityId - {activityId}, activity.Action - {activity.Action}, activity.GetActivityType - {activity.GetActivityType()}, activity.ReplyToId - {activity.ReplyToId}, activity.Text - {activity.Text}, response time: {turn.TurnStart.ElapsedMilliseconds}");

            // This HttpResponseMessage flows back to SendActivityAsync(), not the client that Posted to original message
            // This response lets the bot's SendActivityAsync() know that the channel successfully brokered the
            // response back to the client.
            return Request.CreateResponse(HttpStatusCode.OK, new ResourceResponse(id: Guid.NewGuid().ToString("n")));
        }

        /// <summary>
        /// UpdateActivity
        /// </summary>
        /// <remarks>
        /// Markdown=Content\Methods\UpdateActivity.md
        /// </remarks>
        /// <param name="conversationId">Conversation ID</param>
        /// <param name="activityId">activityId to update</param>
        /// <param name="activity">replacement Activity</param>
        [HttpPut]
        [Route("{conversationId}/activities/{activityId}")]  // Matches 'v3/conversations/{conversationId}/activities/{activityId}'
        public HttpResponseMessage UpdateActivity(string conversationId, string activityId, [FromBody]Activity activity)
        {
            return Request.CreateResponse(HttpStatusCode.OK, new ResourceResponse(activity.Id));
        }

        /// <summary>
        /// DeleteActivity
        /// </summary>
        /// <remarks>
        /// Markdown=Content\Methods\DeleteActivity.md
        /// </remarks>
        /// <param name="conversationId">Conversation ID</param>
        /// <param name="activityId">activityId to delete</param>
        [HttpDelete]
        [Route("{conversationId}/activities/{activityId}")] // Matches 'v3/conversations/{conversationId}/activities/{activityId}'
        public HttpResponseMessage DeleteActivity(string conversationId, string activityId)
        {
            return Request.CreateResponse(HttpStatusCode.OK);
        }

        /// <summary>
        /// GetConversationMembers
        /// </summary>
        /// <remarks>
        /// Markdown=Content\Methods\GetConversationMembers.md
        /// </remarks>
        /// <param name="conversationId">Conversation ID</param>
        [HttpGet]
        [Route("{conversationId}/members")] // Matches 'v3/conversations/{conversationId}/members'
        public HttpResponseMessage GetConversationMembers(string conversationId)
        {
            return Request.CreateResponse(HttpStatusCode.OK, new ChannelAccount[0]);
        }


        /// <summary>
        /// GetActivityMembers
        /// </summary>
        /// <remarks>
        /// Markdown=Content\Methods\GetActivityMembers.md
        /// </remarks>
        /// <param name="conversationId">Conversation ID</param>
        /// <param name="activityId">Activity ID</param>
        [HttpGet]
        [Route("{conversationId}/activities/{activityId}/members")] // Matches 'v3/conversations/{conversationId}/activities/{activityId}/members'
        public HttpResponseMessage GetActivityMembers(string conversationId, string activityId)
        {
            return Request.CreateResponse(HttpStatusCode.OK, new ChannelAccount[0]);
        }

        /// <summary>
        /// UploadAttachment
        /// </summary>
        /// <remarks>
        /// Markdown=Content\Methods\UploadAttachment.md
        /// </remarks>
        /// <param name="conversationId">Conversation ID</param>
        /// <param name="attachmentUpload">Attachment data</param>
        [HttpPost]
        [Route("{conversationId}/attachments")] // Matches 'v3/conversations/{conversationId}/attachments'
        public HttpResponseMessage UploadAttachment(string conversationId, [FromBody]AttachmentData attachmentUpload)
        {
            var id = Guid.NewGuid().ToString("n");
            return Request.CreateResponse(HttpStatusCode.OK, new ResourceResponse(id: id));
        }

        [HttpGet]
        [Route("LoadTestResults")] // Matches 'v3/conversations/LoadTestResults'
        public HttpResponseMessage LoadTestResults()
        {
            var averageResponse = _activityCount == 0 ? 0 : _totalResponseTime / _activityCount;

            string summary = $"Reset of Summary Totals:\n\tTotal conversations: {_conversationCount}\n\tTotal message/activities: {_activityCount}\n\tTotal response time: {_totalResponseTime} milliseconds\n\tOrphand requests: {_turns.Count}\n\tFastest response: {_fastestResponseTime} milliseconds\n\tSlowest response: {_slowestResponseTime} milliseconds\n\tAverage response: {averageResponse} milliseconds\n\tMessages Per Second: {_messagePerSecond}";
            var response = new HttpResponseMessage(HttpStatusCode.OK);

            response.Content = new StringContent(summary, System.Text.Encoding.UTF8, "text/plain");

            return response;
        }

        [HttpGet]
        [Route("ResetTotals")] // Matches 'v3/conversations/ResetTotals'
        public HttpResponseMessage ResetTotals()
        {
            _turns = new Dictionary<long, MockTurn>();
            _conversationCount = 0;
            _activityCount = 0;
            _totalResponseTime = 0;
            _fastestResponseTime = double.MaxValue;
            _slowestResponseTime = 0;
            _startOfTest.Reset();
            _messagePerSecond = 0;

            string summary = $"ummary of load test:\n\tTotal conversations: {_conversationCount}\n\tTotal message/activities: {_activityCount}\n\tTotal response time: {_totalResponseTime} milliseconds\n\tOrphand requests: {_turns.Count}\n\tFastest response: {_fastestResponseTime} milliseconds\n\tSlowest response: {_slowestResponseTime} milliseconds\n\tAverage response: 0 milliseconds";
            var response = new HttpResponseMessage(HttpStatusCode.OK);

            response.Content = new StringContent(summary, System.Text.Encoding.UTF8, "text/plain");

            return response;
        }
    }

    public class TokenResponse
    {
        public string token_type { get; set; }
        public int expires_in { get; set; }
        public int ext_expires_in { get; set; }
        public string access_token { get; set; }
    }

    public class MockTurn
    {
        public Activity Activity { get; set; }
        public Activity BotResponse { get; set; }
        public Stopwatch TurnStart { get; set; } = new Stopwatch();
        public Stopwatch TurnEnd { get; set; } = new Stopwatch();
    }
}
