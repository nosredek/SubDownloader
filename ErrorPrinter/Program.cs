using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Messaging;
using System.Text;
using System.Threading.Tasks;

namespace ErrorPrinter
{
    internal class Program
    {
        private static void Main(string[] args)
        {
            MessageQueue errorQueue = null;
            string ErrorQueuePath = @".\Private$\SubErrors";

            if (MessageQueue.Exists(ErrorQueuePath))
            {
                errorQueue = new MessageQueue(ErrorQueuePath);
                errorQueue.Label = "ErrorQueue";
            }
            else
            {
                MessageQueue.Create(ErrorQueuePath);
                errorQueue = new MessageQueue(ErrorQueuePath);
                errorQueue.Label = "ErrorQ";
            }

            errorQueue.Formatter = new XmlMessageFormatter(new Type[] { typeof(ErrorMessage) });

            var errors = errorQueue.GetAllMessages().Select(i => (ErrorMessage)i.Body).ToList();
            var dict = new Dictionary<DateTime, List<ErrorMessage>>();
            foreach (var t in errors)
            {
                if (dict.ContainsKey(t.Time.Date)) dict[t.Time.Date].Add(t);
                else
                {
                    dict.Add(t.Time.Date, new List<ErrorMessage>());
                    dict[t.Time.Date].Add(t);
                }
            }

            errorQueue.Purge();
            if (errors.Count == 0)
            {
                Console.WriteLine("No errors");
                return;
            }
            foreach (var t in dict)
            {
                File.AppendAllLines($"{Directory.GetCurrentDirectory()}\\Log({t.Key.ToShortDateString().Replace('.', '-').TrimEnd('-')}).txt", t.Value.Select(i => $"{i.Time.ToShortDateString()} {i.Time.ToLongTimeString()} ---- {i.Message}"));
            }

            //File.AppendAllLines($"{Directory.GetCurrentDirectory()}\\errors.csv", errors.Select(i => $"{i.Time.ToShortDateString()} {i.Time.ToLongTimeString()} ---- {i.Message}"));
        }
    }

    public class ErrorMessage
    {
        public string Message { get; set; }
        public DateTime Time { get; set; }
    }
}