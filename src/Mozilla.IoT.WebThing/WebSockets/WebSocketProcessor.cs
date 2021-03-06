using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Net.WebSockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Mozilla.IoT.WebThing.Json;

namespace Mozilla.IoT.WebThing.WebSockets
{
    public class WebSocketProcessor
    {
        private static readonly ArrayPool<byte> s_pool = ArrayPool<byte>.Create();
        private static readonly ArraySegment<byte> s_error = new ArraySegment<byte>(
            Encoding.UTF8.GetBytes(
                @"{""messageType"": ""error"", ""data"": {""status"": ""400 Bad Request"",""message"": ""Invalid message""}}"));

        private readonly IServiceProvider _service;

        public WebSocketProcessor(IServiceProvider service)
        {
            _service = service ?? throw new ArgumentException(nameof(service));
        }

        public async ValueTask ExecuteAsync(Thing thing, WebSocket webSocket, CancellationToken cancellation)
        {
            var id = Guid.NewGuid();
            thing.Subscribers.TryAdd(id, webSocket);

            var executors = _service.GetService<IEnumerable<IWebSocketAction>>();

            var options = _service.GetService<IOptions<WebSocketOptions>>();

            var buffer = s_pool.Rent(options.Value.ReceiveBufferSize);

            try
            {
                var result = await webSocket
                    .ReceiveAsync(new ArraySegment<byte>(buffer), cancellation)
                    .ConfigureAwait(false);
                
                var jsonConvert = _service.GetRequiredService<IJsonSerializer>();
                var jsonValue = _service.GetRequiredService<IJsonValue>();

                while (!result.CloseStatus.HasValue && !cancellation.IsCancellationRequested)
                {
                    var json = jsonConvert.Deserialize<IDictionary<string, object>>(buffer.AsSpan(0, result.Count));

                    if (!json.ContainsKey("messageType") || !json.ContainsKey("data"))
                    {
                        await webSocket.SendAsync(s_error, WebSocketMessageType.Text, true, cancellation);

                        result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);

                        continue;
                    }

                    var type = json["messageType"];
                    var data = json["data"];

                    var action = executors?.FirstOrDefault(x => x.Action == type.ToString());

                    try
                    {
                        if (action != null)
                        {
                            await action.ExecuteAsync(thing, webSocket, jsonValue.GetValue(data, typeof(IDictionary<string, object>)) as IDictionary<string, object>, cancellation);
                        }
                        else
                        {
                            await webSocket.SendAsync(s_error, WebSocketMessageType.Text, true, cancellation);
                        }
                    }
                    catch (Exception e)
                    {
                        Console.WriteLine(e.ToString());
                    }

                    result = await webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);
                }
            }
            finally
            {
                thing.Subscribers.TryRemove(id, out _);
                s_pool.Return(buffer);
            }
        }
    }
}
