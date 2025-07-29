
using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Web;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.AspNetCore.Components;
using System.Threading.Tasks;
using Radzen;

namespace WicsPlatform.Client
{
    public partial class wicsService
    {
        private readonly HttpClient httpClient;
        private readonly Uri baseUri;
        private readonly NavigationManager navigationManager;

        public wicsService(NavigationManager navigationManager, HttpClient httpClient, IConfiguration configuration)
        {
            this.httpClient = httpClient;

            this.navigationManager = navigationManager;
            this.baseUri = new Uri($"{navigationManager.BaseUri}odata/wics/");
        }


        public async System.Threading.Tasks.Task ExportBroadcastsToExcel(Query query = null, string fileName = null)
        {
            navigationManager.NavigateTo(query != null ? query.ToUrl($"export/wics/broadcasts/excel(fileName='{(!string.IsNullOrEmpty(fileName) ? UrlEncoder.Default.Encode(fileName) : "Export")}')") : $"export/wics/broadcasts/excel(fileName='{(!string.IsNullOrEmpty(fileName) ? UrlEncoder.Default.Encode(fileName) : "Export")}')", true);
        }

        public async System.Threading.Tasks.Task ExportBroadcastsToCSV(Query query = null, string fileName = null)
        {
            navigationManager.NavigateTo(query != null ? query.ToUrl($"export/wics/broadcasts/csv(fileName='{(!string.IsNullOrEmpty(fileName) ? UrlEncoder.Default.Encode(fileName) : "Export")}')") : $"export/wics/broadcasts/csv(fileName='{(!string.IsNullOrEmpty(fileName) ? UrlEncoder.Default.Encode(fileName) : "Export")}')", true);
        }

        partial void OnGetBroadcasts(HttpRequestMessage requestMessage);

        public async Task<Radzen.ODataServiceResult<WicsPlatform.Server.Models.wics.Broadcast>> GetBroadcasts(Query query)
        {
            return await GetBroadcasts(filter:$"{query.Filter}", orderby:$"{query.OrderBy}", top:query.Top, skip:query.Skip, count:query.Top != null && query.Skip != null);
        }

        public async Task<Radzen.ODataServiceResult<WicsPlatform.Server.Models.wics.Broadcast>> GetBroadcasts(string filter = default(string), string orderby = default(string), string expand = default(string), int? top = default(int?), int? skip = default(int?), bool? count = default(bool?), string format = default(string), string select = default(string), string apply = default(string))
        {
            var uri = new Uri(baseUri, $"Broadcasts");
            uri = Radzen.ODataExtensions.GetODataUri(uri: uri, filter:filter, top:top, skip:skip, orderby:orderby, expand:expand, select:select, count:count, apply:apply);

            var httpRequestMessage = new HttpRequestMessage(HttpMethod.Get, uri);

            OnGetBroadcasts(httpRequestMessage);

            var response = await httpClient.SendAsync(httpRequestMessage);

            return await Radzen.HttpResponseMessageExtensions.ReadAsync<Radzen.ODataServiceResult<WicsPlatform.Server.Models.wics.Broadcast>>(response);
        }

        partial void OnCreateBroadcast(HttpRequestMessage requestMessage);

        public async Task<WicsPlatform.Server.Models.wics.Broadcast> CreateBroadcast(WicsPlatform.Server.Models.wics.Broadcast broadcast = default(WicsPlatform.Server.Models.wics.Broadcast))
        {
            var uri = new Uri(baseUri, $"Broadcasts");

            var httpRequestMessage = new HttpRequestMessage(HttpMethod.Post, uri);

            httpRequestMessage.Content = new StringContent(Radzen.ODataJsonSerializer.Serialize(broadcast), Encoding.UTF8, "application/json");

            OnCreateBroadcast(httpRequestMessage);

            var response = await httpClient.SendAsync(httpRequestMessage);

            return await Radzen.HttpResponseMessageExtensions.ReadAsync<WicsPlatform.Server.Models.wics.Broadcast>(response);
        }

        partial void OnDeleteBroadcast(HttpRequestMessage requestMessage);

        public async Task<HttpResponseMessage> DeleteBroadcast(ulong id = default(ulong))
        {
            var uri = new Uri(baseUri, $"Broadcasts({id})");

            var httpRequestMessage = new HttpRequestMessage(HttpMethod.Delete, uri);

            OnDeleteBroadcast(httpRequestMessage);

            return await httpClient.SendAsync(httpRequestMessage);
        }

        partial void OnGetBroadcastById(HttpRequestMessage requestMessage);

        public async Task<WicsPlatform.Server.Models.wics.Broadcast> GetBroadcastById(string expand = default(string), ulong id = default(ulong))
        {
            var uri = new Uri(baseUri, $"Broadcasts({id})");

            uri = Radzen.ODataExtensions.GetODataUri(uri: uri, filter:null, top:null, skip:null, orderby:null, expand:expand, select:null, count:null);

            var httpRequestMessage = new HttpRequestMessage(HttpMethod.Get, uri);

            OnGetBroadcastById(httpRequestMessage);

            var response = await httpClient.SendAsync(httpRequestMessage);

            return await Radzen.HttpResponseMessageExtensions.ReadAsync<WicsPlatform.Server.Models.wics.Broadcast>(response);
        }

        partial void OnUpdateBroadcast(HttpRequestMessage requestMessage);
        
        public async Task<HttpResponseMessage> UpdateBroadcast(ulong id = default(ulong), WicsPlatform.Server.Models.wics.Broadcast broadcast = default(WicsPlatform.Server.Models.wics.Broadcast))
        {
            var uri = new Uri(baseUri, $"Broadcasts({id})");

            var httpRequestMessage = new HttpRequestMessage(HttpMethod.Patch, uri);


            httpRequestMessage.Content = new StringContent(Radzen.ODataJsonSerializer.Serialize(broadcast), Encoding.UTF8, "application/json");

            OnUpdateBroadcast(httpRequestMessage);

            return await httpClient.SendAsync(httpRequestMessage);
        }

        public async System.Threading.Tasks.Task ExportChannelsToExcel(Query query = null, string fileName = null)
        {
            navigationManager.NavigateTo(query != null ? query.ToUrl($"export/wics/channels/excel(fileName='{(!string.IsNullOrEmpty(fileName) ? UrlEncoder.Default.Encode(fileName) : "Export")}')") : $"export/wics/channels/excel(fileName='{(!string.IsNullOrEmpty(fileName) ? UrlEncoder.Default.Encode(fileName) : "Export")}')", true);
        }

        public async System.Threading.Tasks.Task ExportChannelsToCSV(Query query = null, string fileName = null)
        {
            navigationManager.NavigateTo(query != null ? query.ToUrl($"export/wics/channels/csv(fileName='{(!string.IsNullOrEmpty(fileName) ? UrlEncoder.Default.Encode(fileName) : "Export")}')") : $"export/wics/channels/csv(fileName='{(!string.IsNullOrEmpty(fileName) ? UrlEncoder.Default.Encode(fileName) : "Export")}')", true);
        }

        partial void OnGetChannels(HttpRequestMessage requestMessage);

        public async Task<Radzen.ODataServiceResult<WicsPlatform.Server.Models.wics.Channel>> GetChannels(Query query)
        {
            return await GetChannels(filter:$"{query.Filter}", orderby:$"{query.OrderBy}", top:query.Top, skip:query.Skip, count:query.Top != null && query.Skip != null);
        }

        public async Task<Radzen.ODataServiceResult<WicsPlatform.Server.Models.wics.Channel>> GetChannels(string filter = default(string), string orderby = default(string), string expand = default(string), int? top = default(int?), int? skip = default(int?), bool? count = default(bool?), string format = default(string), string select = default(string), string apply = default(string))
        {
            var uri = new Uri(baseUri, $"Channels");
            uri = Radzen.ODataExtensions.GetODataUri(uri: uri, filter:filter, top:top, skip:skip, orderby:orderby, expand:expand, select:select, count:count, apply:apply);

            var httpRequestMessage = new HttpRequestMessage(HttpMethod.Get, uri);

            OnGetChannels(httpRequestMessage);

            var response = await httpClient.SendAsync(httpRequestMessage);

            return await Radzen.HttpResponseMessageExtensions.ReadAsync<Radzen.ODataServiceResult<WicsPlatform.Server.Models.wics.Channel>>(response);
        }

        partial void OnCreateChannel(HttpRequestMessage requestMessage);

        public async Task<WicsPlatform.Server.Models.wics.Channel> CreateChannel(WicsPlatform.Server.Models.wics.Channel channel = default(WicsPlatform.Server.Models.wics.Channel))
        {
            var uri = new Uri(baseUri, $"Channels");

            var httpRequestMessage = new HttpRequestMessage(HttpMethod.Post, uri);

            httpRequestMessage.Content = new StringContent(Radzen.ODataJsonSerializer.Serialize(channel), Encoding.UTF8, "application/json");

            OnCreateChannel(httpRequestMessage);

            var response = await httpClient.SendAsync(httpRequestMessage);

            return await Radzen.HttpResponseMessageExtensions.ReadAsync<WicsPlatform.Server.Models.wics.Channel>(response);
        }

        partial void OnDeleteChannel(HttpRequestMessage requestMessage);

        public async Task<HttpResponseMessage> DeleteChannel(ulong id = default(ulong))
        {
            var uri = new Uri(baseUri, $"Channels({id})");

            var httpRequestMessage = new HttpRequestMessage(HttpMethod.Delete, uri);

            OnDeleteChannel(httpRequestMessage);

            return await httpClient.SendAsync(httpRequestMessage);
        }

        partial void OnGetChannelById(HttpRequestMessage requestMessage);

        public async Task<WicsPlatform.Server.Models.wics.Channel> GetChannelById(string expand = default(string), ulong id = default(ulong))
        {
            var uri = new Uri(baseUri, $"Channels({id})");

            uri = Radzen.ODataExtensions.GetODataUri(uri: uri, filter:null, top:null, skip:null, orderby:null, expand:expand, select:null, count:null);

            var httpRequestMessage = new HttpRequestMessage(HttpMethod.Get, uri);

            OnGetChannelById(httpRequestMessage);

            var response = await httpClient.SendAsync(httpRequestMessage);

            return await Radzen.HttpResponseMessageExtensions.ReadAsync<WicsPlatform.Server.Models.wics.Channel>(response);
        }

        partial void OnUpdateChannel(HttpRequestMessage requestMessage);
        
        public async Task<HttpResponseMessage> UpdateChannel(ulong id = default(ulong), WicsPlatform.Server.Models.wics.Channel channel = default(WicsPlatform.Server.Models.wics.Channel))
        {
            var uri = new Uri(baseUri, $"Channels({id})");

            var httpRequestMessage = new HttpRequestMessage(HttpMethod.Patch, uri);


            httpRequestMessage.Content = new StringContent(Radzen.ODataJsonSerializer.Serialize(channel), Encoding.UTF8, "application/json");

            OnUpdateChannel(httpRequestMessage);

            return await httpClient.SendAsync(httpRequestMessage);
        }

        public async System.Threading.Tasks.Task ExportGroupsToExcel(Query query = null, string fileName = null)
        {
            navigationManager.NavigateTo(query != null ? query.ToUrl($"export/wics/groups/excel(fileName='{(!string.IsNullOrEmpty(fileName) ? UrlEncoder.Default.Encode(fileName) : "Export")}')") : $"export/wics/groups/excel(fileName='{(!string.IsNullOrEmpty(fileName) ? UrlEncoder.Default.Encode(fileName) : "Export")}')", true);
        }

        public async System.Threading.Tasks.Task ExportGroupsToCSV(Query query = null, string fileName = null)
        {
            navigationManager.NavigateTo(query != null ? query.ToUrl($"export/wics/groups/csv(fileName='{(!string.IsNullOrEmpty(fileName) ? UrlEncoder.Default.Encode(fileName) : "Export")}')") : $"export/wics/groups/csv(fileName='{(!string.IsNullOrEmpty(fileName) ? UrlEncoder.Default.Encode(fileName) : "Export")}')", true);
        }

        partial void OnGetGroups(HttpRequestMessage requestMessage);

        public async Task<Radzen.ODataServiceResult<WicsPlatform.Server.Models.wics.Group>> GetGroups(Query query)
        {
            return await GetGroups(filter:$"{query.Filter}", orderby:$"{query.OrderBy}", top:query.Top, skip:query.Skip, count:query.Top != null && query.Skip != null);
        }

        public async Task<Radzen.ODataServiceResult<WicsPlatform.Server.Models.wics.Group>> GetGroups(string filter = default(string), string orderby = default(string), string expand = default(string), int? top = default(int?), int? skip = default(int?), bool? count = default(bool?), string format = default(string), string select = default(string), string apply = default(string))
        {
            var uri = new Uri(baseUri, $"Groups");
            uri = Radzen.ODataExtensions.GetODataUri(uri: uri, filter:filter, top:top, skip:skip, orderby:orderby, expand:expand, select:select, count:count, apply:apply);

            var httpRequestMessage = new HttpRequestMessage(HttpMethod.Get, uri);

            OnGetGroups(httpRequestMessage);

            var response = await httpClient.SendAsync(httpRequestMessage);

            return await Radzen.HttpResponseMessageExtensions.ReadAsync<Radzen.ODataServiceResult<WicsPlatform.Server.Models.wics.Group>>(response);
        }

        partial void OnCreateGroup(HttpRequestMessage requestMessage);

        public async Task<WicsPlatform.Server.Models.wics.Group> CreateGroup(WicsPlatform.Server.Models.wics.Group _group = default(WicsPlatform.Server.Models.wics.Group))
        {
            var uri = new Uri(baseUri, $"Groups");

            var httpRequestMessage = new HttpRequestMessage(HttpMethod.Post, uri);

            httpRequestMessage.Content = new StringContent(Radzen.ODataJsonSerializer.Serialize(_group), Encoding.UTF8, "application/json");

            OnCreateGroup(httpRequestMessage);

            var response = await httpClient.SendAsync(httpRequestMessage);

            return await Radzen.HttpResponseMessageExtensions.ReadAsync<WicsPlatform.Server.Models.wics.Group>(response);
        }

        partial void OnDeleteGroup(HttpRequestMessage requestMessage);

        public async Task<HttpResponseMessage> DeleteGroup(ulong id = default(ulong))
        {
            var uri = new Uri(baseUri, $"Groups({id})");

            var httpRequestMessage = new HttpRequestMessage(HttpMethod.Delete, uri);

            OnDeleteGroup(httpRequestMessage);

            return await httpClient.SendAsync(httpRequestMessage);
        }

        partial void OnGetGroupById(HttpRequestMessage requestMessage);

        public async Task<WicsPlatform.Server.Models.wics.Group> GetGroupById(string expand = default(string), ulong id = default(ulong))
        {
            var uri = new Uri(baseUri, $"Groups({id})");

            uri = Radzen.ODataExtensions.GetODataUri(uri: uri, filter:null, top:null, skip:null, orderby:null, expand:expand, select:null, count:null);

            var httpRequestMessage = new HttpRequestMessage(HttpMethod.Get, uri);

            OnGetGroupById(httpRequestMessage);

            var response = await httpClient.SendAsync(httpRequestMessage);

            return await Radzen.HttpResponseMessageExtensions.ReadAsync<WicsPlatform.Server.Models.wics.Group>(response);
        }

        partial void OnUpdateGroup(HttpRequestMessage requestMessage);
        
        public async Task<HttpResponseMessage> UpdateGroup(ulong id = default(ulong), WicsPlatform.Server.Models.wics.Group _group = default(WicsPlatform.Server.Models.wics.Group))
        {
            var uri = new Uri(baseUri, $"Groups({id})");

            var httpRequestMessage = new HttpRequestMessage(HttpMethod.Patch, uri);


            httpRequestMessage.Content = new StringContent(Radzen.ODataJsonSerializer.Serialize(_group), Encoding.UTF8, "application/json");

            OnUpdateGroup(httpRequestMessage);

            return await httpClient.SendAsync(httpRequestMessage);
        }

        public async System.Threading.Tasks.Task ExportMapChannelMediaToExcel(Query query = null, string fileName = null)
        {
            navigationManager.NavigateTo(query != null ? query.ToUrl($"export/wics/mapchannelmedia/excel(fileName='{(!string.IsNullOrEmpty(fileName) ? UrlEncoder.Default.Encode(fileName) : "Export")}')") : $"export/wics/mapchannelmedia/excel(fileName='{(!string.IsNullOrEmpty(fileName) ? UrlEncoder.Default.Encode(fileName) : "Export")}')", true);
        }

        public async System.Threading.Tasks.Task ExportMapChannelMediaToCSV(Query query = null, string fileName = null)
        {
            navigationManager.NavigateTo(query != null ? query.ToUrl($"export/wics/mapchannelmedia/csv(fileName='{(!string.IsNullOrEmpty(fileName) ? UrlEncoder.Default.Encode(fileName) : "Export")}')") : $"export/wics/mapchannelmedia/csv(fileName='{(!string.IsNullOrEmpty(fileName) ? UrlEncoder.Default.Encode(fileName) : "Export")}')", true);
        }

        partial void OnGetMapChannelMedia(HttpRequestMessage requestMessage);

        public async Task<Radzen.ODataServiceResult<WicsPlatform.Server.Models.wics.MapChannelMedium>> GetMapChannelMedia(Query query)
        {
            return await GetMapChannelMedia(filter:$"{query.Filter}", orderby:$"{query.OrderBy}", top:query.Top, skip:query.Skip, count:query.Top != null && query.Skip != null);
        }

        public async Task<Radzen.ODataServiceResult<WicsPlatform.Server.Models.wics.MapChannelMedium>> GetMapChannelMedia(string filter = default(string), string orderby = default(string), string expand = default(string), int? top = default(int?), int? skip = default(int?), bool? count = default(bool?), string format = default(string), string select = default(string), string apply = default(string))
        {
            var uri = new Uri(baseUri, $"MapChannelMedia");
            uri = Radzen.ODataExtensions.GetODataUri(uri: uri, filter:filter, top:top, skip:skip, orderby:orderby, expand:expand, select:select, count:count, apply:apply);

            var httpRequestMessage = new HttpRequestMessage(HttpMethod.Get, uri);

            OnGetMapChannelMedia(httpRequestMessage);

            var response = await httpClient.SendAsync(httpRequestMessage);

            return await Radzen.HttpResponseMessageExtensions.ReadAsync<Radzen.ODataServiceResult<WicsPlatform.Server.Models.wics.MapChannelMedium>>(response);
        }

        partial void OnCreateMapChannelMedium(HttpRequestMessage requestMessage);

        public async Task<WicsPlatform.Server.Models.wics.MapChannelMedium> CreateMapChannelMedium(WicsPlatform.Server.Models.wics.MapChannelMedium mapChannelMedium = default(WicsPlatform.Server.Models.wics.MapChannelMedium))
        {
            var uri = new Uri(baseUri, $"MapChannelMedia");

            var httpRequestMessage = new HttpRequestMessage(HttpMethod.Post, uri);

            httpRequestMessage.Content = new StringContent(Radzen.ODataJsonSerializer.Serialize(mapChannelMedium), Encoding.UTF8, "application/json");

            OnCreateMapChannelMedium(httpRequestMessage);

            var response = await httpClient.SendAsync(httpRequestMessage);

            return await Radzen.HttpResponseMessageExtensions.ReadAsync<WicsPlatform.Server.Models.wics.MapChannelMedium>(response);
        }

        partial void OnDeleteMapChannelMedium(HttpRequestMessage requestMessage);

        public async Task<HttpResponseMessage> DeleteMapChannelMedium(ulong id = default(ulong))
        {
            var uri = new Uri(baseUri, $"MapChannelMedia({id})");

            var httpRequestMessage = new HttpRequestMessage(HttpMethod.Delete, uri);

            OnDeleteMapChannelMedium(httpRequestMessage);

            return await httpClient.SendAsync(httpRequestMessage);
        }

        partial void OnGetMapChannelMediumById(HttpRequestMessage requestMessage);

        public async Task<WicsPlatform.Server.Models.wics.MapChannelMedium> GetMapChannelMediumById(string expand = default(string), ulong id = default(ulong))
        {
            var uri = new Uri(baseUri, $"MapChannelMedia({id})");

            uri = Radzen.ODataExtensions.GetODataUri(uri: uri, filter:null, top:null, skip:null, orderby:null, expand:expand, select:null, count:null);

            var httpRequestMessage = new HttpRequestMessage(HttpMethod.Get, uri);

            OnGetMapChannelMediumById(httpRequestMessage);

            var response = await httpClient.SendAsync(httpRequestMessage);

            return await Radzen.HttpResponseMessageExtensions.ReadAsync<WicsPlatform.Server.Models.wics.MapChannelMedium>(response);
        }

        partial void OnUpdateMapChannelMedium(HttpRequestMessage requestMessage);
        
        public async Task<HttpResponseMessage> UpdateMapChannelMedium(ulong id = default(ulong), WicsPlatform.Server.Models.wics.MapChannelMedium mapChannelMedium = default(WicsPlatform.Server.Models.wics.MapChannelMedium))
        {
            var uri = new Uri(baseUri, $"MapChannelMedia({id})");

            var httpRequestMessage = new HttpRequestMessage(HttpMethod.Patch, uri);


            httpRequestMessage.Content = new StringContent(Radzen.ODataJsonSerializer.Serialize(mapChannelMedium), Encoding.UTF8, "application/json");

            OnUpdateMapChannelMedium(httpRequestMessage);

            return await httpClient.SendAsync(httpRequestMessage);
        }

        public async System.Threading.Tasks.Task ExportMapChannelTtsToExcel(Query query = null, string fileName = null)
        {
            navigationManager.NavigateTo(query != null ? query.ToUrl($"export/wics/mapchanneltts/excel(fileName='{(!string.IsNullOrEmpty(fileName) ? UrlEncoder.Default.Encode(fileName) : "Export")}')") : $"export/wics/mapchanneltts/excel(fileName='{(!string.IsNullOrEmpty(fileName) ? UrlEncoder.Default.Encode(fileName) : "Export")}')", true);
        }

        public async System.Threading.Tasks.Task ExportMapChannelTtsToCSV(Query query = null, string fileName = null)
        {
            navigationManager.NavigateTo(query != null ? query.ToUrl($"export/wics/mapchanneltts/csv(fileName='{(!string.IsNullOrEmpty(fileName) ? UrlEncoder.Default.Encode(fileName) : "Export")}')") : $"export/wics/mapchanneltts/csv(fileName='{(!string.IsNullOrEmpty(fileName) ? UrlEncoder.Default.Encode(fileName) : "Export")}')", true);
        }

        partial void OnGetMapChannelTts(HttpRequestMessage requestMessage);

        public async Task<Radzen.ODataServiceResult<WicsPlatform.Server.Models.wics.MapChannelTt>> GetMapChannelTts(Query query)
        {
            return await GetMapChannelTts(filter:$"{query.Filter}", orderby:$"{query.OrderBy}", top:query.Top, skip:query.Skip, count:query.Top != null && query.Skip != null);
        }

        public async Task<Radzen.ODataServiceResult<WicsPlatform.Server.Models.wics.MapChannelTt>> GetMapChannelTts(string filter = default(string), string orderby = default(string), string expand = default(string), int? top = default(int?), int? skip = default(int?), bool? count = default(bool?), string format = default(string), string select = default(string), string apply = default(string))
        {
            var uri = new Uri(baseUri, $"MapChannelTts");
            uri = Radzen.ODataExtensions.GetODataUri(uri: uri, filter:filter, top:top, skip:skip, orderby:orderby, expand:expand, select:select, count:count, apply:apply);

            var httpRequestMessage = new HttpRequestMessage(HttpMethod.Get, uri);

            OnGetMapChannelTts(httpRequestMessage);

            var response = await httpClient.SendAsync(httpRequestMessage);

            return await Radzen.HttpResponseMessageExtensions.ReadAsync<Radzen.ODataServiceResult<WicsPlatform.Server.Models.wics.MapChannelTt>>(response);
        }

        partial void OnCreateMapChannelTt(HttpRequestMessage requestMessage);

        public async Task<WicsPlatform.Server.Models.wics.MapChannelTt> CreateMapChannelTt(WicsPlatform.Server.Models.wics.MapChannelTt mapChannelTt = default(WicsPlatform.Server.Models.wics.MapChannelTt))
        {
            var uri = new Uri(baseUri, $"MapChannelTts");

            var httpRequestMessage = new HttpRequestMessage(HttpMethod.Post, uri);

            httpRequestMessage.Content = new StringContent(Radzen.ODataJsonSerializer.Serialize(mapChannelTt), Encoding.UTF8, "application/json");

            OnCreateMapChannelTt(httpRequestMessage);

            var response = await httpClient.SendAsync(httpRequestMessage);

            return await Radzen.HttpResponseMessageExtensions.ReadAsync<WicsPlatform.Server.Models.wics.MapChannelTt>(response);
        }

        partial void OnDeleteMapChannelTt(HttpRequestMessage requestMessage);

        public async Task<HttpResponseMessage> DeleteMapChannelTt(ulong id = default(ulong))
        {
            var uri = new Uri(baseUri, $"MapChannelTts({id})");

            var httpRequestMessage = new HttpRequestMessage(HttpMethod.Delete, uri);

            OnDeleteMapChannelTt(httpRequestMessage);

            return await httpClient.SendAsync(httpRequestMessage);
        }

        partial void OnGetMapChannelTtById(HttpRequestMessage requestMessage);

        public async Task<WicsPlatform.Server.Models.wics.MapChannelTt> GetMapChannelTtById(string expand = default(string), ulong id = default(ulong))
        {
            var uri = new Uri(baseUri, $"MapChannelTts({id})");

            uri = Radzen.ODataExtensions.GetODataUri(uri: uri, filter:null, top:null, skip:null, orderby:null, expand:expand, select:null, count:null);

            var httpRequestMessage = new HttpRequestMessage(HttpMethod.Get, uri);

            OnGetMapChannelTtById(httpRequestMessage);

            var response = await httpClient.SendAsync(httpRequestMessage);

            return await Radzen.HttpResponseMessageExtensions.ReadAsync<WicsPlatform.Server.Models.wics.MapChannelTt>(response);
        }

        partial void OnUpdateMapChannelTt(HttpRequestMessage requestMessage);
        
        public async Task<HttpResponseMessage> UpdateMapChannelTt(ulong id = default(ulong), WicsPlatform.Server.Models.wics.MapChannelTt mapChannelTt = default(WicsPlatform.Server.Models.wics.MapChannelTt))
        {
            var uri = new Uri(baseUri, $"MapChannelTts({id})");

            var httpRequestMessage = new HttpRequestMessage(HttpMethod.Patch, uri);


            httpRequestMessage.Content = new StringContent(Radzen.ODataJsonSerializer.Serialize(mapChannelTt), Encoding.UTF8, "application/json");

            OnUpdateMapChannelTt(httpRequestMessage);

            return await httpClient.SendAsync(httpRequestMessage);
        }

        public async System.Threading.Tasks.Task ExportMapMediaGroupsToExcel(Query query = null, string fileName = null)
        {
            navigationManager.NavigateTo(query != null ? query.ToUrl($"export/wics/mapmediagroups/excel(fileName='{(!string.IsNullOrEmpty(fileName) ? UrlEncoder.Default.Encode(fileName) : "Export")}')") : $"export/wics/mapmediagroups/excel(fileName='{(!string.IsNullOrEmpty(fileName) ? UrlEncoder.Default.Encode(fileName) : "Export")}')", true);
        }

        public async System.Threading.Tasks.Task ExportMapMediaGroupsToCSV(Query query = null, string fileName = null)
        {
            navigationManager.NavigateTo(query != null ? query.ToUrl($"export/wics/mapmediagroups/csv(fileName='{(!string.IsNullOrEmpty(fileName) ? UrlEncoder.Default.Encode(fileName) : "Export")}')") : $"export/wics/mapmediagroups/csv(fileName='{(!string.IsNullOrEmpty(fileName) ? UrlEncoder.Default.Encode(fileName) : "Export")}')", true);
        }

        partial void OnGetMapMediaGroups(HttpRequestMessage requestMessage);

        public async Task<Radzen.ODataServiceResult<WicsPlatform.Server.Models.wics.MapMediaGroup>> GetMapMediaGroups(Query query)
        {
            return await GetMapMediaGroups(filter:$"{query.Filter}", orderby:$"{query.OrderBy}", top:query.Top, skip:query.Skip, count:query.Top != null && query.Skip != null);
        }

        public async Task<Radzen.ODataServiceResult<WicsPlatform.Server.Models.wics.MapMediaGroup>> GetMapMediaGroups(string filter = default(string), string orderby = default(string), string expand = default(string), int? top = default(int?), int? skip = default(int?), bool? count = default(bool?), string format = default(string), string select = default(string), string apply = default(string))
        {
            var uri = new Uri(baseUri, $"MapMediaGroups");
            uri = Radzen.ODataExtensions.GetODataUri(uri: uri, filter:filter, top:top, skip:skip, orderby:orderby, expand:expand, select:select, count:count, apply:apply);

            var httpRequestMessage = new HttpRequestMessage(HttpMethod.Get, uri);

            OnGetMapMediaGroups(httpRequestMessage);

            var response = await httpClient.SendAsync(httpRequestMessage);

            return await Radzen.HttpResponseMessageExtensions.ReadAsync<Radzen.ODataServiceResult<WicsPlatform.Server.Models.wics.MapMediaGroup>>(response);
        }

        partial void OnCreateMapMediaGroup(HttpRequestMessage requestMessage);

        public async Task<WicsPlatform.Server.Models.wics.MapMediaGroup> CreateMapMediaGroup(WicsPlatform.Server.Models.wics.MapMediaGroup mapMediaGroup = default(WicsPlatform.Server.Models.wics.MapMediaGroup))
        {
            var uri = new Uri(baseUri, $"MapMediaGroups");

            var httpRequestMessage = new HttpRequestMessage(HttpMethod.Post, uri);

            httpRequestMessage.Content = new StringContent(Radzen.ODataJsonSerializer.Serialize(mapMediaGroup), Encoding.UTF8, "application/json");

            OnCreateMapMediaGroup(httpRequestMessage);

            var response = await httpClient.SendAsync(httpRequestMessage);

            return await Radzen.HttpResponseMessageExtensions.ReadAsync<WicsPlatform.Server.Models.wics.MapMediaGroup>(response);
        }

        partial void OnDeleteMapMediaGroup(HttpRequestMessage requestMessage);

        public async Task<HttpResponseMessage> DeleteMapMediaGroup(ulong id = default(ulong))
        {
            var uri = new Uri(baseUri, $"MapMediaGroups({id})");

            var httpRequestMessage = new HttpRequestMessage(HttpMethod.Delete, uri);

            OnDeleteMapMediaGroup(httpRequestMessage);

            return await httpClient.SendAsync(httpRequestMessage);
        }

        partial void OnGetMapMediaGroupById(HttpRequestMessage requestMessage);

        public async Task<WicsPlatform.Server.Models.wics.MapMediaGroup> GetMapMediaGroupById(string expand = default(string), ulong id = default(ulong))
        {
            var uri = new Uri(baseUri, $"MapMediaGroups({id})");

            uri = Radzen.ODataExtensions.GetODataUri(uri: uri, filter:null, top:null, skip:null, orderby:null, expand:expand, select:null, count:null);

            var httpRequestMessage = new HttpRequestMessage(HttpMethod.Get, uri);

            OnGetMapMediaGroupById(httpRequestMessage);

            var response = await httpClient.SendAsync(httpRequestMessage);

            return await Radzen.HttpResponseMessageExtensions.ReadAsync<WicsPlatform.Server.Models.wics.MapMediaGroup>(response);
        }

        partial void OnUpdateMapMediaGroup(HttpRequestMessage requestMessage);
        
        public async Task<HttpResponseMessage> UpdateMapMediaGroup(ulong id = default(ulong), WicsPlatform.Server.Models.wics.MapMediaGroup mapMediaGroup = default(WicsPlatform.Server.Models.wics.MapMediaGroup))
        {
            var uri = new Uri(baseUri, $"MapMediaGroups({id})");

            var httpRequestMessage = new HttpRequestMessage(HttpMethod.Patch, uri);


            httpRequestMessage.Content = new StringContent(Radzen.ODataJsonSerializer.Serialize(mapMediaGroup), Encoding.UTF8, "application/json");

            OnUpdateMapMediaGroup(httpRequestMessage);

            return await httpClient.SendAsync(httpRequestMessage);
        }

        public async System.Threading.Tasks.Task ExportMapSpeakerGroupsToExcel(Query query = null, string fileName = null)
        {
            navigationManager.NavigateTo(query != null ? query.ToUrl($"export/wics/mapspeakergroups/excel(fileName='{(!string.IsNullOrEmpty(fileName) ? UrlEncoder.Default.Encode(fileName) : "Export")}')") : $"export/wics/mapspeakergroups/excel(fileName='{(!string.IsNullOrEmpty(fileName) ? UrlEncoder.Default.Encode(fileName) : "Export")}')", true);
        }

        public async System.Threading.Tasks.Task ExportMapSpeakerGroupsToCSV(Query query = null, string fileName = null)
        {
            navigationManager.NavigateTo(query != null ? query.ToUrl($"export/wics/mapspeakergroups/csv(fileName='{(!string.IsNullOrEmpty(fileName) ? UrlEncoder.Default.Encode(fileName) : "Export")}')") : $"export/wics/mapspeakergroups/csv(fileName='{(!string.IsNullOrEmpty(fileName) ? UrlEncoder.Default.Encode(fileName) : "Export")}')", true);
        }

        partial void OnGetMapSpeakerGroups(HttpRequestMessage requestMessage);

        public async Task<Radzen.ODataServiceResult<WicsPlatform.Server.Models.wics.MapSpeakerGroup>> GetMapSpeakerGroups(Query query)
        {
            return await GetMapSpeakerGroups(filter:$"{query.Filter}", orderby:$"{query.OrderBy}", top:query.Top, skip:query.Skip, count:query.Top != null && query.Skip != null);
        }

        public async Task<Radzen.ODataServiceResult<WicsPlatform.Server.Models.wics.MapSpeakerGroup>> GetMapSpeakerGroups(string filter = default(string), string orderby = default(string), string expand = default(string), int? top = default(int?), int? skip = default(int?), bool? count = default(bool?), string format = default(string), string select = default(string), string apply = default(string))
        {
            var uri = new Uri(baseUri, $"MapSpeakerGroups");
            uri = Radzen.ODataExtensions.GetODataUri(uri: uri, filter:filter, top:top, skip:skip, orderby:orderby, expand:expand, select:select, count:count, apply:apply);

            var httpRequestMessage = new HttpRequestMessage(HttpMethod.Get, uri);

            OnGetMapSpeakerGroups(httpRequestMessage);

            var response = await httpClient.SendAsync(httpRequestMessage);

            return await Radzen.HttpResponseMessageExtensions.ReadAsync<Radzen.ODataServiceResult<WicsPlatform.Server.Models.wics.MapSpeakerGroup>>(response);
        }

        partial void OnCreateMapSpeakerGroup(HttpRequestMessage requestMessage);

        public async Task<WicsPlatform.Server.Models.wics.MapSpeakerGroup> CreateMapSpeakerGroup(WicsPlatform.Server.Models.wics.MapSpeakerGroup mapSpeakerGroup = default(WicsPlatform.Server.Models.wics.MapSpeakerGroup))
        {
            var uri = new Uri(baseUri, $"MapSpeakerGroups");

            var httpRequestMessage = new HttpRequestMessage(HttpMethod.Post, uri);

            httpRequestMessage.Content = new StringContent(Radzen.ODataJsonSerializer.Serialize(mapSpeakerGroup), Encoding.UTF8, "application/json");

            OnCreateMapSpeakerGroup(httpRequestMessage);

            var response = await httpClient.SendAsync(httpRequestMessage);

            return await Radzen.HttpResponseMessageExtensions.ReadAsync<WicsPlatform.Server.Models.wics.MapSpeakerGroup>(response);
        }

        partial void OnDeleteMapSpeakerGroup(HttpRequestMessage requestMessage);

        public async Task<HttpResponseMessage> DeleteMapSpeakerGroup(ulong id = default(ulong))
        {
            var uri = new Uri(baseUri, $"MapSpeakerGroups({id})");

            var httpRequestMessage = new HttpRequestMessage(HttpMethod.Delete, uri);

            OnDeleteMapSpeakerGroup(httpRequestMessage);

            return await httpClient.SendAsync(httpRequestMessage);
        }

        partial void OnGetMapSpeakerGroupById(HttpRequestMessage requestMessage);

        public async Task<WicsPlatform.Server.Models.wics.MapSpeakerGroup> GetMapSpeakerGroupById(string expand = default(string), ulong id = default(ulong))
        {
            var uri = new Uri(baseUri, $"MapSpeakerGroups({id})");

            uri = Radzen.ODataExtensions.GetODataUri(uri: uri, filter:null, top:null, skip:null, orderby:null, expand:expand, select:null, count:null);

            var httpRequestMessage = new HttpRequestMessage(HttpMethod.Get, uri);

            OnGetMapSpeakerGroupById(httpRequestMessage);

            var response = await httpClient.SendAsync(httpRequestMessage);

            return await Radzen.HttpResponseMessageExtensions.ReadAsync<WicsPlatform.Server.Models.wics.MapSpeakerGroup>(response);
        }

        partial void OnUpdateMapSpeakerGroup(HttpRequestMessage requestMessage);
        
        public async Task<HttpResponseMessage> UpdateMapSpeakerGroup(ulong id = default(ulong), WicsPlatform.Server.Models.wics.MapSpeakerGroup mapSpeakerGroup = default(WicsPlatform.Server.Models.wics.MapSpeakerGroup))
        {
            var uri = new Uri(baseUri, $"MapSpeakerGroups({id})");

            var httpRequestMessage = new HttpRequestMessage(HttpMethod.Patch, uri);


            httpRequestMessage.Content = new StringContent(Radzen.ODataJsonSerializer.Serialize(mapSpeakerGroup), Encoding.UTF8, "application/json");

            OnUpdateMapSpeakerGroup(httpRequestMessage);

            return await httpClient.SendAsync(httpRequestMessage);
        }

        public async System.Threading.Tasks.Task ExportMediaToExcel(Query query = null, string fileName = null)
        {
            navigationManager.NavigateTo(query != null ? query.ToUrl($"export/wics/media/excel(fileName='{(!string.IsNullOrEmpty(fileName) ? UrlEncoder.Default.Encode(fileName) : "Export")}')") : $"export/wics/media/excel(fileName='{(!string.IsNullOrEmpty(fileName) ? UrlEncoder.Default.Encode(fileName) : "Export")}')", true);
        }

        public async System.Threading.Tasks.Task ExportMediaToCSV(Query query = null, string fileName = null)
        {
            navigationManager.NavigateTo(query != null ? query.ToUrl($"export/wics/media/csv(fileName='{(!string.IsNullOrEmpty(fileName) ? UrlEncoder.Default.Encode(fileName) : "Export")}')") : $"export/wics/media/csv(fileName='{(!string.IsNullOrEmpty(fileName) ? UrlEncoder.Default.Encode(fileName) : "Export")}')", true);
        }

        partial void OnGetMedia(HttpRequestMessage requestMessage);

        public async Task<Radzen.ODataServiceResult<WicsPlatform.Server.Models.wics.Medium>> GetMedia(Query query)
        {
            return await GetMedia(filter:$"{query.Filter}", orderby:$"{query.OrderBy}", top:query.Top, skip:query.Skip, count:query.Top != null && query.Skip != null);
        }

        public async Task<Radzen.ODataServiceResult<WicsPlatform.Server.Models.wics.Medium>> GetMedia(string filter = default(string), string orderby = default(string), string expand = default(string), int? top = default(int?), int? skip = default(int?), bool? count = default(bool?), string format = default(string), string select = default(string), string apply = default(string))
        {
            var uri = new Uri(baseUri, $"Media");
            uri = Radzen.ODataExtensions.GetODataUri(uri: uri, filter:filter, top:top, skip:skip, orderby:orderby, expand:expand, select:select, count:count, apply:apply);

            var httpRequestMessage = new HttpRequestMessage(HttpMethod.Get, uri);

            OnGetMedia(httpRequestMessage);

            var response = await httpClient.SendAsync(httpRequestMessage);

            return await Radzen.HttpResponseMessageExtensions.ReadAsync<Radzen.ODataServiceResult<WicsPlatform.Server.Models.wics.Medium>>(response);
        }

        partial void OnCreateMedium(HttpRequestMessage requestMessage);

        public async Task<WicsPlatform.Server.Models.wics.Medium> CreateMedium(WicsPlatform.Server.Models.wics.Medium medium = default(WicsPlatform.Server.Models.wics.Medium))
        {
            var uri = new Uri(baseUri, $"Media");

            var httpRequestMessage = new HttpRequestMessage(HttpMethod.Post, uri);

            httpRequestMessage.Content = new StringContent(Radzen.ODataJsonSerializer.Serialize(medium), Encoding.UTF8, "application/json");

            OnCreateMedium(httpRequestMessage);

            var response = await httpClient.SendAsync(httpRequestMessage);

            return await Radzen.HttpResponseMessageExtensions.ReadAsync<WicsPlatform.Server.Models.wics.Medium>(response);
        }

        partial void OnDeleteMedium(HttpRequestMessage requestMessage);

        public async Task<HttpResponseMessage> DeleteMedium(ulong id = default(ulong))
        {
            var uri = new Uri(baseUri, $"Media({id})");

            var httpRequestMessage = new HttpRequestMessage(HttpMethod.Delete, uri);

            OnDeleteMedium(httpRequestMessage);

            return await httpClient.SendAsync(httpRequestMessage);
        }

        partial void OnGetMediumById(HttpRequestMessage requestMessage);

        public async Task<WicsPlatform.Server.Models.wics.Medium> GetMediumById(string expand = default(string), ulong id = default(ulong))
        {
            var uri = new Uri(baseUri, $"Media({id})");

            uri = Radzen.ODataExtensions.GetODataUri(uri: uri, filter:null, top:null, skip:null, orderby:null, expand:expand, select:null, count:null);

            var httpRequestMessage = new HttpRequestMessage(HttpMethod.Get, uri);

            OnGetMediumById(httpRequestMessage);

            var response = await httpClient.SendAsync(httpRequestMessage);

            return await Radzen.HttpResponseMessageExtensions.ReadAsync<WicsPlatform.Server.Models.wics.Medium>(response);
        }

        partial void OnUpdateMedium(HttpRequestMessage requestMessage);
        
        public async Task<HttpResponseMessage> UpdateMedium(ulong id = default(ulong), WicsPlatform.Server.Models.wics.Medium medium = default(WicsPlatform.Server.Models.wics.Medium))
        {
            var uri = new Uri(baseUri, $"Media({id})");

            var httpRequestMessage = new HttpRequestMessage(HttpMethod.Patch, uri);


            httpRequestMessage.Content = new StringContent(Radzen.ODataJsonSerializer.Serialize(medium), Encoding.UTF8, "application/json");

            OnUpdateMedium(httpRequestMessage);

            return await httpClient.SendAsync(httpRequestMessage);
        }

        public async System.Threading.Tasks.Task ExportMicsToExcel(Query query = null, string fileName = null)
        {
            navigationManager.NavigateTo(query != null ? query.ToUrl($"export/wics/mics/excel(fileName='{(!string.IsNullOrEmpty(fileName) ? UrlEncoder.Default.Encode(fileName) : "Export")}')") : $"export/wics/mics/excel(fileName='{(!string.IsNullOrEmpty(fileName) ? UrlEncoder.Default.Encode(fileName) : "Export")}')", true);
        }

        public async System.Threading.Tasks.Task ExportMicsToCSV(Query query = null, string fileName = null)
        {
            navigationManager.NavigateTo(query != null ? query.ToUrl($"export/wics/mics/csv(fileName='{(!string.IsNullOrEmpty(fileName) ? UrlEncoder.Default.Encode(fileName) : "Export")}')") : $"export/wics/mics/csv(fileName='{(!string.IsNullOrEmpty(fileName) ? UrlEncoder.Default.Encode(fileName) : "Export")}')", true);
        }

        partial void OnGetMics(HttpRequestMessage requestMessage);

        public async Task<Radzen.ODataServiceResult<WicsPlatform.Server.Models.wics.Mic>> GetMics(Query query)
        {
            return await GetMics(filter:$"{query.Filter}", orderby:$"{query.OrderBy}", top:query.Top, skip:query.Skip, count:query.Top != null && query.Skip != null);
        }

        public async Task<Radzen.ODataServiceResult<WicsPlatform.Server.Models.wics.Mic>> GetMics(string filter = default(string), string orderby = default(string), string expand = default(string), int? top = default(int?), int? skip = default(int?), bool? count = default(bool?), string format = default(string), string select = default(string), string apply = default(string))
        {
            var uri = new Uri(baseUri, $"Mics");
            uri = Radzen.ODataExtensions.GetODataUri(uri: uri, filter:filter, top:top, skip:skip, orderby:orderby, expand:expand, select:select, count:count, apply:apply);

            var httpRequestMessage = new HttpRequestMessage(HttpMethod.Get, uri);

            OnGetMics(httpRequestMessage);

            var response = await httpClient.SendAsync(httpRequestMessage);

            return await Radzen.HttpResponseMessageExtensions.ReadAsync<Radzen.ODataServiceResult<WicsPlatform.Server.Models.wics.Mic>>(response);
        }

        partial void OnCreateMic(HttpRequestMessage requestMessage);

        public async Task<WicsPlatform.Server.Models.wics.Mic> CreateMic(WicsPlatform.Server.Models.wics.Mic mic = default(WicsPlatform.Server.Models.wics.Mic))
        {
            var uri = new Uri(baseUri, $"Mics");

            var httpRequestMessage = new HttpRequestMessage(HttpMethod.Post, uri);

            httpRequestMessage.Content = new StringContent(Radzen.ODataJsonSerializer.Serialize(mic), Encoding.UTF8, "application/json");

            OnCreateMic(httpRequestMessage);

            var response = await httpClient.SendAsync(httpRequestMessage);

            return await Radzen.HttpResponseMessageExtensions.ReadAsync<WicsPlatform.Server.Models.wics.Mic>(response);
        }

        partial void OnDeleteMic(HttpRequestMessage requestMessage);

        public async Task<HttpResponseMessage> DeleteMic(ulong id = default(ulong))
        {
            var uri = new Uri(baseUri, $"Mics({id})");

            var httpRequestMessage = new HttpRequestMessage(HttpMethod.Delete, uri);

            OnDeleteMic(httpRequestMessage);

            return await httpClient.SendAsync(httpRequestMessage);
        }

        partial void OnGetMicById(HttpRequestMessage requestMessage);

        public async Task<WicsPlatform.Server.Models.wics.Mic> GetMicById(string expand = default(string), ulong id = default(ulong))
        {
            var uri = new Uri(baseUri, $"Mics({id})");

            uri = Radzen.ODataExtensions.GetODataUri(uri: uri, filter:null, top:null, skip:null, orderby:null, expand:expand, select:null, count:null);

            var httpRequestMessage = new HttpRequestMessage(HttpMethod.Get, uri);

            OnGetMicById(httpRequestMessage);

            var response = await httpClient.SendAsync(httpRequestMessage);

            return await Radzen.HttpResponseMessageExtensions.ReadAsync<WicsPlatform.Server.Models.wics.Mic>(response);
        }

        partial void OnUpdateMic(HttpRequestMessage requestMessage);
        
        public async Task<HttpResponseMessage> UpdateMic(ulong id = default(ulong), WicsPlatform.Server.Models.wics.Mic mic = default(WicsPlatform.Server.Models.wics.Mic))
        {
            var uri = new Uri(baseUri, $"Mics({id})");

            var httpRequestMessage = new HttpRequestMessage(HttpMethod.Patch, uri);


            httpRequestMessage.Content = new StringContent(Radzen.ODataJsonSerializer.Serialize(mic), Encoding.UTF8, "application/json");

            OnUpdateMic(httpRequestMessage);

            return await httpClient.SendAsync(httpRequestMessage);
        }

        public async System.Threading.Tasks.Task ExportSpeakersToExcel(Query query = null, string fileName = null)
        {
            navigationManager.NavigateTo(query != null ? query.ToUrl($"export/wics/speakers/excel(fileName='{(!string.IsNullOrEmpty(fileName) ? UrlEncoder.Default.Encode(fileName) : "Export")}')") : $"export/wics/speakers/excel(fileName='{(!string.IsNullOrEmpty(fileName) ? UrlEncoder.Default.Encode(fileName) : "Export")}')", true);
        }

        public async System.Threading.Tasks.Task ExportSpeakersToCSV(Query query = null, string fileName = null)
        {
            navigationManager.NavigateTo(query != null ? query.ToUrl($"export/wics/speakers/csv(fileName='{(!string.IsNullOrEmpty(fileName) ? UrlEncoder.Default.Encode(fileName) : "Export")}')") : $"export/wics/speakers/csv(fileName='{(!string.IsNullOrEmpty(fileName) ? UrlEncoder.Default.Encode(fileName) : "Export")}')", true);
        }

        partial void OnGetSpeakers(HttpRequestMessage requestMessage);

        public async Task<Radzen.ODataServiceResult<WicsPlatform.Server.Models.wics.Speaker>> GetSpeakers(Query query)
        {
            return await GetSpeakers(filter:$"{query.Filter}", orderby:$"{query.OrderBy}", top:query.Top, skip:query.Skip, count:query.Top != null && query.Skip != null);
        }

        public async Task<Radzen.ODataServiceResult<WicsPlatform.Server.Models.wics.Speaker>> GetSpeakers(string filter = default(string), string orderby = default(string), string expand = default(string), int? top = default(int?), int? skip = default(int?), bool? count = default(bool?), string format = default(string), string select = default(string), string apply = default(string))
        {
            var uri = new Uri(baseUri, $"Speakers");
            uri = Radzen.ODataExtensions.GetODataUri(uri: uri, filter:filter, top:top, skip:skip, orderby:orderby, expand:expand, select:select, count:count, apply:apply);

            var httpRequestMessage = new HttpRequestMessage(HttpMethod.Get, uri);

            OnGetSpeakers(httpRequestMessage);

            var response = await httpClient.SendAsync(httpRequestMessage);

            return await Radzen.HttpResponseMessageExtensions.ReadAsync<Radzen.ODataServiceResult<WicsPlatform.Server.Models.wics.Speaker>>(response);
        }

        partial void OnCreateSpeaker(HttpRequestMessage requestMessage);

        public async Task<WicsPlatform.Server.Models.wics.Speaker> CreateSpeaker(WicsPlatform.Server.Models.wics.Speaker speaker = default(WicsPlatform.Server.Models.wics.Speaker))
        {
            var uri = new Uri(baseUri, $"Speakers");

            var httpRequestMessage = new HttpRequestMessage(HttpMethod.Post, uri);

            httpRequestMessage.Content = new StringContent(Radzen.ODataJsonSerializer.Serialize(speaker), Encoding.UTF8, "application/json");

            OnCreateSpeaker(httpRequestMessage);

            var response = await httpClient.SendAsync(httpRequestMessage);

            return await Radzen.HttpResponseMessageExtensions.ReadAsync<WicsPlatform.Server.Models.wics.Speaker>(response);
        }

        partial void OnDeleteSpeaker(HttpRequestMessage requestMessage);

        public async Task<HttpResponseMessage> DeleteSpeaker(ulong id = default(ulong))
        {
            var uri = new Uri(baseUri, $"Speakers({id})");

            var httpRequestMessage = new HttpRequestMessage(HttpMethod.Delete, uri);

            OnDeleteSpeaker(httpRequestMessage);

            return await httpClient.SendAsync(httpRequestMessage);
        }

        partial void OnGetSpeakerById(HttpRequestMessage requestMessage);

        public async Task<WicsPlatform.Server.Models.wics.Speaker> GetSpeakerById(string expand = default(string), ulong id = default(ulong))
        {
            var uri = new Uri(baseUri, $"Speakers({id})");

            uri = Radzen.ODataExtensions.GetODataUri(uri: uri, filter:null, top:null, skip:null, orderby:null, expand:expand, select:null, count:null);

            var httpRequestMessage = new HttpRequestMessage(HttpMethod.Get, uri);

            OnGetSpeakerById(httpRequestMessage);

            var response = await httpClient.SendAsync(httpRequestMessage);

            return await Radzen.HttpResponseMessageExtensions.ReadAsync<WicsPlatform.Server.Models.wics.Speaker>(response);
        }

        partial void OnUpdateSpeaker(HttpRequestMessage requestMessage);
        
        public async Task<HttpResponseMessage> UpdateSpeaker(ulong id = default(ulong), WicsPlatform.Server.Models.wics.Speaker speaker = default(WicsPlatform.Server.Models.wics.Speaker))
        {
            var uri = new Uri(baseUri, $"Speakers({id})");

            var httpRequestMessage = new HttpRequestMessage(HttpMethod.Patch, uri);


            httpRequestMessage.Content = new StringContent(Radzen.ODataJsonSerializer.Serialize(speaker), Encoding.UTF8, "application/json");

            OnUpdateSpeaker(httpRequestMessage);

            return await httpClient.SendAsync(httpRequestMessage);
        }

        public async System.Threading.Tasks.Task ExportTtsToExcel(Query query = null, string fileName = null)
        {
            navigationManager.NavigateTo(query != null ? query.ToUrl($"export/wics/tts/excel(fileName='{(!string.IsNullOrEmpty(fileName) ? UrlEncoder.Default.Encode(fileName) : "Export")}')") : $"export/wics/tts/excel(fileName='{(!string.IsNullOrEmpty(fileName) ? UrlEncoder.Default.Encode(fileName) : "Export")}')", true);
        }

        public async System.Threading.Tasks.Task ExportTtsToCSV(Query query = null, string fileName = null)
        {
            navigationManager.NavigateTo(query != null ? query.ToUrl($"export/wics/tts/csv(fileName='{(!string.IsNullOrEmpty(fileName) ? UrlEncoder.Default.Encode(fileName) : "Export")}')") : $"export/wics/tts/csv(fileName='{(!string.IsNullOrEmpty(fileName) ? UrlEncoder.Default.Encode(fileName) : "Export")}')", true);
        }

        partial void OnGetTts(HttpRequestMessage requestMessage);

        public async Task<Radzen.ODataServiceResult<WicsPlatform.Server.Models.wics.Tt>> GetTts(Query query)
        {
            return await GetTts(filter:$"{query.Filter}", orderby:$"{query.OrderBy}", top:query.Top, skip:query.Skip, count:query.Top != null && query.Skip != null);
        }

        public async Task<Radzen.ODataServiceResult<WicsPlatform.Server.Models.wics.Tt>> GetTts(string filter = default(string), string orderby = default(string), string expand = default(string), int? top = default(int?), int? skip = default(int?), bool? count = default(bool?), string format = default(string), string select = default(string), string apply = default(string))
        {
            var uri = new Uri(baseUri, $"Tts");
            uri = Radzen.ODataExtensions.GetODataUri(uri: uri, filter:filter, top:top, skip:skip, orderby:orderby, expand:expand, select:select, count:count, apply:apply);

            var httpRequestMessage = new HttpRequestMessage(HttpMethod.Get, uri);

            OnGetTts(httpRequestMessage);

            var response = await httpClient.SendAsync(httpRequestMessage);

            return await Radzen.HttpResponseMessageExtensions.ReadAsync<Radzen.ODataServiceResult<WicsPlatform.Server.Models.wics.Tt>>(response);
        }

        partial void OnCreateTt(HttpRequestMessage requestMessage);

        public async Task<WicsPlatform.Server.Models.wics.Tt> CreateTt(WicsPlatform.Server.Models.wics.Tt tt = default(WicsPlatform.Server.Models.wics.Tt))
        {
            var uri = new Uri(baseUri, $"Tts");

            var httpRequestMessage = new HttpRequestMessage(HttpMethod.Post, uri);

            httpRequestMessage.Content = new StringContent(Radzen.ODataJsonSerializer.Serialize(tt), Encoding.UTF8, "application/json");

            OnCreateTt(httpRequestMessage);

            var response = await httpClient.SendAsync(httpRequestMessage);

            return await Radzen.HttpResponseMessageExtensions.ReadAsync<WicsPlatform.Server.Models.wics.Tt>(response);
        }

        partial void OnDeleteTt(HttpRequestMessage requestMessage);

        public async Task<HttpResponseMessage> DeleteTt(ulong id = default(ulong))
        {
            var uri = new Uri(baseUri, $"Tts({id})");

            var httpRequestMessage = new HttpRequestMessage(HttpMethod.Delete, uri);

            OnDeleteTt(httpRequestMessage);

            return await httpClient.SendAsync(httpRequestMessage);
        }

        partial void OnGetTtById(HttpRequestMessage requestMessage);

        public async Task<WicsPlatform.Server.Models.wics.Tt> GetTtById(string expand = default(string), ulong id = default(ulong))
        {
            var uri = new Uri(baseUri, $"Tts({id})");

            uri = Radzen.ODataExtensions.GetODataUri(uri: uri, filter:null, top:null, skip:null, orderby:null, expand:expand, select:null, count:null);

            var httpRequestMessage = new HttpRequestMessage(HttpMethod.Get, uri);

            OnGetTtById(httpRequestMessage);

            var response = await httpClient.SendAsync(httpRequestMessage);

            return await Radzen.HttpResponseMessageExtensions.ReadAsync<WicsPlatform.Server.Models.wics.Tt>(response);
        }

        partial void OnUpdateTt(HttpRequestMessage requestMessage);
        
        public async Task<HttpResponseMessage> UpdateTt(ulong id = default(ulong), WicsPlatform.Server.Models.wics.Tt tt = default(WicsPlatform.Server.Models.wics.Tt))
        {
            var uri = new Uri(baseUri, $"Tts({id})");

            var httpRequestMessage = new HttpRequestMessage(HttpMethod.Patch, uri);


            httpRequestMessage.Content = new StringContent(Radzen.ODataJsonSerializer.Serialize(tt), Encoding.UTF8, "application/json");

            OnUpdateTt(httpRequestMessage);

            return await httpClient.SendAsync(httpRequestMessage);
        }
    }
}