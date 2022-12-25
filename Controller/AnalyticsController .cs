using HtmlAgilityPack;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Configuration;
using Redis.Application;
using Redis.Model;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Xml.Linq;


namespace Redis.Controller
{
    [Route("[controller]")]
    [ApiController]
    public class AnalyticsController : ControllerBase
    {
        readonly CultureInfo culture = new("en-US");
        private readonly MyDbContext _dbContext;
        private readonly IConfiguration _configuration;
        private static readonly object _lockObj = new();
        private readonly IDistributedCache _cache;
        public AnalyticsController(MyDbContext context, IConfiguration configuration, IDistributedCache cache)
        {
            _dbContext = context;
            _configuration = configuration;
            _cache = cache;
        }

        [HttpPost]
        [Route("CreatePosts/{authorId}")]
        public async Task<bool> CreatePosts(string authorId)
        {
            try
            {
                var entries = new List<Feed>();
                for(int j = 1; j <= 100; j++)
                {
                    Feed feed = new Feed
                    {
                        Content = "Check",
                        Link = "Data",
                        PubDate = DateTime.Now,
                        Title = "Yogen",
                        FeedType = "Articles",
                        Author = "Yogen"
                    };
                    entries.Add(feed);
                }

                List<Feed> feeds = entries.OrderByDescending(o => o.PubDate).ToList();
                string urlAddress = string.Empty;
                List<ArticleMatrix> articleMatrices = new();
                foreach(var item in feeds)
                {
                    ArticleMatrix articleMatrix = new()
                    {
                        AuthorId = authorId,
                        Author = item.Author,
                        Type = item.FeedType,
                        Link = item.Link,
                        Title = item.Title,
                        PubDate = item.PubDate
                    };
                    articleMatrices.Add(articleMatrix);
                }
                _dbContext.ArticleMatrices.RemoveRange(_dbContext.ArticleMatrices.Where(x => x.AuthorId == authorId));

                foreach (ArticleMatrix articleMatrix in articleMatrices)
                {
                    await _dbContext.ArticleMatrices.AddAsync(articleMatrix);
                }


                await _dbContext.SaveChangesAsync();
                await _cache.RemoveAsync(authorId);
                return true;
            }
            catch(Exception ex)
            {
                return false;
            }
        }

        [HttpGet]
        [Route("GetAll/{authorId}/{enableCache}")]
        public async Task<List<ArticleMatrix>> GetAll(string authorId, bool enableCache)
        {
            if (!enableCache)
            {
                return _dbContext.ArticleMatrices.Where(x => x.AuthorId == authorId).OrderByDescending(x => x.PubDate).ToList();
            }
            string cacheKey = authorId;

            // Trying to get data from the Redis cache
            byte[] cachedData = await _cache.GetAsync(cacheKey);
            List<ArticleMatrix> articleMatrices = new();
            if (cachedData != null)
            {
                // If the data is found in the cache, encode and deserialize cached data.
                var cachedDataString = Encoding.UTF8.GetString(cachedData);
                articleMatrices = JsonSerializer.Deserialize<List<ArticleMatrix>>(cachedDataString);
            }
            else
            {
                // If the data is not found in the cache, then fetch data from database
                articleMatrices = _dbContext.ArticleMatrices.Where(x => x.AuthorId == authorId).OrderByDescending(x => x.PubDate).ToList();

                // Serializing the data
                string cachedDataString = JsonSerializer.Serialize(articleMatrices);
                var dataToCache = Encoding.UTF8.GetBytes(cachedDataString);

                // Setting up the cache options
                DistributedCacheEntryOptions options = new DistributedCacheEntryOptions()
                    .SetAbsoluteExpiration(DateTime.Now.AddMinutes(5))
                    .SetSlidingExpiration(TimeSpan.FromMinutes(3));

                // Add the data into the cache
                await _cache.SetAsync(cacheKey, dataToCache, options);
            }
            return articleMatrices;
        }
    }
}
