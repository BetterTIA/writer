using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;

namespace TiaMcpServer.Siemens
{
    /// <summary>
    /// The reason behind a failed write. Openness usually puts it into InnerException and the
    /// outer message only says "Failed importing ...", so a tool error ends with
    /// 'errorDetail: ["outer", "inner", ...]': every message of the exception chain, outermost
    /// first, without repeats, plus any import messages the API handed back.
    /// Pure, so Test15ErrorDetail covers it without TIA Portal.
    /// </summary>
    public static class ErrorDetail
    {
        public const string Label = "errorDetail";

        /// <summary>The messages of the exception and of every InnerException below it.</summary>
        public static List<string> Chain(Exception? exception)
        {
            var messages = new List<string>();

            for (var e = exception; e != null; e = e.InnerException)
            {
                if (e is AggregateException aggregate)
                {
                    messages.AddRange(aggregate.InnerExceptions.SelectMany(Chain));
                }

                var text = (e.Message ?? string.Empty).Trim();

                if (text.Length > 0 && !messages.Contains(text))
                {
                    messages.Add(text);
                }
            }

            return messages;
        }

        /// <summary>
        /// <paramref name="message"/> followed by 'errorDetail: [...]' with the chain of
        /// <paramref name="exception"/> and <paramref name="extra"/> (e.g. import messages);
        /// the message is returned alone when there is nothing to add.
        /// </summary>
        public static string Append(string message, Exception? exception, IEnumerable<string>? extra = null)
        {
            var details = Chain(exception).Concat(extra ?? Enumerable.Empty<string>())
                .Where(d => !string.IsNullOrWhiteSpace(d) && message.IndexOf(d, StringComparison.Ordinal) < 0)
                .Distinct()
                .ToList();

            if (details.Count == 0)
            {
                return message;
            }

            var json = JsonSerializer.Serialize(details, new JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });

            return $"{message} {Label}: {json}";
        }
    }
}
