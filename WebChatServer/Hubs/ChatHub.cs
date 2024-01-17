using Microsoft.AspNetCore.SignalR;
using Microsoft.Azure.Cosmos.Table;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using WindowsAzure.Table.Extensions;

namespace WebChatServer.Hubs
{
    public class ChatHub : Hub
    {
        private static CloudStorageAccount storageAccount = CloudStorageAccount.Parse("DefaultEndpointsProtocol=https;AccountName=qgcstorage;AccountKey=dWNom2FBAdbgKxyOk1IqL3iFGM1Ki7A+nr+4PhtgKA42Kvd9DuqmVVMT3MWd76Dh7YJB+b2XYZ6T+AStoEzTiQ==;EndpointSuffix=core.windows.net");
        private static CloudTableClient tableClient = storageAccount.CreateCloudTableClient(new TableClientConfiguration());
        private static CloudTable chatTable = tableClient.GetTableReference("ChatMessages");

        private static List<Message> chatMessages = new List<Message>();
        private static Dictionary<string, string> connectedClients = new Dictionary<string, string>();

        public async Task SendMessage(string user, string message)
        {
            await Clients.All.SendAsync("ReceiveMessage", user, message);

            // Store the message in Azure Table Storage
            var chatMessage = new ChatMessageEntity(user, message);
            var insertOperation = TableOperation.Insert(chatMessage);
            await chatTable.ExecuteAsync(insertOperation);

            // Add the message to the in-memory list as well
            chatMessages.Add(new Message { User = user, Text = message, Timestamp = DateTime.Now });
        }
        public async Task JoinChat(string user, string message)
        {
            connectedClients[Context.ConnectionId] = user;
            await Clients.Others.SendAsync("ReceiveMessage", user, message);
            chatMessages.Add(new Message { User = user, Text = message, Timestamp = DateTime.Now });
        }

        private async Task LeaveChat()
        {
            if (connectedClients.TryGetValue(Context.ConnectionId, out string user))
            {
                var message = $"{user} left the chat";
                await Clients.Others.SendAsync("ReceiveMessage", user, message);
            }
        }

        public override async Task OnDisconnectedAsync(Exception? exception)
        {
            await LeaveChat();
            await base.OnDisconnectedAsync(exception);

        }

        public async Task<IEnumerable<Message>> GetChatHistory()
        {
            // Retrieve chat messages from Azure Table Storage
            var query = new TableQuery();
            var chatMessagesFromTable = await chatTable.ExecuteQueryAsync(query);

            // Merge in-memory messages with those from table storage
            var allMessages = chatMessages.Concat(chatMessagesFromTable.Select(cm => new Message
            {
                User = cm.Properties["User"].StringValue,
                Text = cm.Properties["Text"].StringValue,
                Timestamp = cm.Properties["Timestamp"].DateTime ?? DateTime.Now
            }));

            return allMessages;
        }
    }

    public class Message
    {
        public string User { get; set; }
        public string Text { get; set; }
        public DateTime Timestamp { get; set; }
    }

    public class ChatMessageEntity : TableEntity
    {
        public ChatMessageEntity(string user, string text)
        {
            PartitionKey = "Chat";
            RowKey = Guid.NewGuid().ToString();
            User = user;
            Text = text;
            Timestamp = DateTime.Now;
        }

        public ChatMessageEntity() { }

        public string User { get; set; }
        public string Text { get; set; }
        public DateTime Timestamp { get; set; }
    }
}
