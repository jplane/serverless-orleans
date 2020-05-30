using System;
using System.Threading;
using System.Threading.Tasks;
using System.Net.Http;
using System.Collections.Concurrent;
using Microsoft.Extensions.Configuration;
using Azure.Storage.Queues;
using System.Linq;
using System.Text;

namespace TestClient
{
    class Program
    {
        private static ConcurrentBag<long> _actorIds = new ConcurrentBag<long>();
        private static Random _rand = new Random(Environment.TickCount);

        static void Main(string[] args)
        {
             var config = new ConfigurationBuilder()
                .AddJsonFile("appsettings.json")
                .Build();
            
            var cts = new CancellationTokenSource();

            var writeTask = WriteMessages(config, cts.Token);

            var readTask = ReadMessages(config, cts.Token);

            Console.WriteLine("Hit enter to quit");

            Console.ReadLine();

            cts.Cancel();

            Task.WaitAll(writeTask, readTask);
        }

        private static long GetRandomActorId()
        {
            var reuseId = _rand.Next(1, 11) > 3;   // 70% reuse existing id

            if (reuseId && _actorIds.TryPeek(out long id))
            {
                return id;
            }
            else
            {
                id = _rand.NextLong(0, long.MaxValue);

                _actorIds.Add(id);

                return id;
            }
        }

        private static string GetRandomString()
        {
            const string chars = "abcdefghijklmnopqrstuvwxyz0123456789";

            var length = _rand.Next(5, 100);

            return new string(
                    Enumerable.Repeat(chars, length)
                              .Select(s => s[_rand.Next(s.Length)])
                              .ToArray());
        }

        private static async Task WriteMessages(IConfiguration config, CancellationToken token)
        {
            var connectionString = config["storageConnectionString"];

            var queue = new QueueClient(connectionString, "input");

            await queue.CreateIfNotExistsAsync();

            while(! token.IsCancellationRequested)
            {
                try	
                {
                    var actorId = GetRandomActorId();
                    var message = GetRandomString();
                    var jsonbytes = Encoding.UTF8.GetBytes($"{{ \"actorId\": {actorId}, \"message\": \"{message}\" }}");
                    var jsonbase64 = Convert.ToBase64String(jsonbytes);
                    await queue.SendMessageAsync(jsonbase64);
                }
                catch(Exception e)
                {
                    Console.WriteLine("Write exception: {0} ", e.Message);
                }

                await Task.Delay(_rand.Next(200, 2000));
            }
        }

        private static async Task ReadMessages(IConfiguration config, CancellationToken token)
        {
            var http = new HttpClient();

            var uri = config["api"];

            while(! token.IsCancellationRequested)
            {
                try	
                {
                    var actorId = GetRandomActorId();
                    var response = await http.GetStringAsync($"{uri}/{actorId}");
                    Console.WriteLine($"READ: {actorId} {response}");
                }
                catch(Exception e)
                {
                    Console.WriteLine("Read exception: {0} ", e.Message);
                }

                await Task.Delay(_rand.Next(200, 2000));
            }
        }
    }

    // https://stackoverflow.com/a/13095144
    static class RandomExtensionMethods
    {
        /// <summary>
        /// Returns a random long from min (inclusive) to max (exclusive)
        /// </summary>
        /// <param name="random">The given random instance</param>
        /// <param name="min">The inclusive minimum bound</param>
        /// <param name="max">The exclusive maximum bound.  Must be greater than min</param>
        public static long NextLong(this Random random, long min, long max)
        {
            if (max <= min)
                throw new ArgumentOutOfRangeException("max", "max must be > min!");

            //Working with ulong so that modulo works correctly with values > long.MaxValue
            ulong uRange = (ulong)(max - min);

            //Prevent a modolo bias; see https://stackoverflow.com/a/10984975/238419
            //for more information.
            //In the worst case, the expected number of calls is 2 (though usually it's
            //much closer to 1) so this loop doesn't really hurt performance at all.
            ulong ulongRand;
            do
            {
                byte[] buf = new byte[8];
                random.NextBytes(buf);
                ulongRand = (ulong)BitConverter.ToInt64(buf, 0);
            } while (ulongRand > ulong.MaxValue - ((ulong.MaxValue % uRange) + 1) % uRange);

            return (long)(ulongRand % uRange) + min;
        }

        /// <summary>
        /// Returns a random long from 0 (inclusive) to max (exclusive)
        /// </summary>
        /// <param name="random">The given random instance</param>
        /// <param name="max">The exclusive maximum bound.  Must be greater than 0</param>
        public static long NextLong(this Random random, long max)
        {
            return random.NextLong(0, max);
        }

        /// <summary>
        /// Returns a random long over all possible values of long (except long.MaxValue, similar to
        /// random.Next())
        /// </summary>
        /// <param name="random">The given random instance</param>
        public static long NextLong(this Random random)
        {
            return random.NextLong(long.MinValue, long.MaxValue);
        }
    }
}
