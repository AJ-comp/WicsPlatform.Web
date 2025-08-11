using System;
using System.Data;
using System.Linq;
using System.Linq.Dynamic.Core;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Components;
using Microsoft.EntityFrameworkCore;
using Radzen;

using WicsPlatform.Server.Data;

namespace WicsPlatform.Server
{
    public partial class wicsService
    {
        wicsContext Context
        {
           get
           {
             return this.context;
           }
        }

        private readonly wicsContext context;
        private readonly NavigationManager navigationManager;

        public wicsService(wicsContext context, NavigationManager navigationManager)
        {
            this.context = context;
            this.navigationManager = navigationManager;
        }

        public void Reset() => Context.ChangeTracker.Entries().Where(e => e.Entity != null).ToList().ForEach(e => e.State = EntityState.Detached);

        public void ApplyQuery<T>(ref IQueryable<T> items, Query query = null)
        {
            if (query != null)
            {
                if (!string.IsNullOrEmpty(query.Filter))
                {
                    if (query.FilterParameters != null)
                    {
                        items = items.Where(query.Filter, query.FilterParameters);
                    }
                    else
                    {
                        items = items.Where(query.Filter);
                    }
                }

                if (!string.IsNullOrEmpty(query.OrderBy))
                {
                    items = items.OrderBy(query.OrderBy);
                }

                if (query.Skip.HasValue)
                {
                    items = items.Skip(query.Skip.Value);
                }

                if (query.Top.HasValue)
                {
                    items = items.Take(query.Top.Value);
                }
            }
        }


        public async Task ExportBroadcastsToExcel(Query query = null, string fileName = null)
        {
            navigationManager.NavigateTo(query != null ? query.ToUrl($"export/wics/broadcasts/excel(fileName='{(!string.IsNullOrEmpty(fileName) ? UrlEncoder.Default.Encode(fileName) : "Export")}')") : $"export/wics/broadcasts/excel(fileName='{(!string.IsNullOrEmpty(fileName) ? UrlEncoder.Default.Encode(fileName) : "Export")}')", true);
        }

        public async Task ExportBroadcastsToCSV(Query query = null, string fileName = null)
        {
            navigationManager.NavigateTo(query != null ? query.ToUrl($"export/wics/broadcasts/csv(fileName='{(!string.IsNullOrEmpty(fileName) ? UrlEncoder.Default.Encode(fileName) : "Export")}')") : $"export/wics/broadcasts/csv(fileName='{(!string.IsNullOrEmpty(fileName) ? UrlEncoder.Default.Encode(fileName) : "Export")}')", true);
        }

        partial void OnBroadcastsRead(ref IQueryable<WicsPlatform.Server.Models.wics.Broadcast> items);

        public async Task<IQueryable<WicsPlatform.Server.Models.wics.Broadcast>> GetBroadcasts(Query query = null)
        {
            var items = Context.Broadcasts.AsQueryable();

            items = items.Include(i => i.Channel);

            if (query != null)
            {
                if (!string.IsNullOrEmpty(query.Expand))
                {
                    var propertiesToExpand = query.Expand.Split(',');
                    foreach(var p in propertiesToExpand)
                    {
                        items = items.Include(p.Trim());
                    }
                }

                ApplyQuery(ref items, query);
            }

            OnBroadcastsRead(ref items);

            return await Task.FromResult(items);
        }

        partial void OnBroadcastGet(WicsPlatform.Server.Models.wics.Broadcast item);
        partial void OnGetBroadcastById(ref IQueryable<WicsPlatform.Server.Models.wics.Broadcast> items);


        public async Task<WicsPlatform.Server.Models.wics.Broadcast> GetBroadcastById(ulong id)
        {
            var items = Context.Broadcasts
                              .AsNoTracking()
                              .Where(i => i.Id == id);

            items = items.Include(i => i.Channel);
 
            OnGetBroadcastById(ref items);

            var itemToReturn = items.FirstOrDefault();

            OnBroadcastGet(itemToReturn);

            return await Task.FromResult(itemToReturn);
        }

        partial void OnBroadcastCreated(WicsPlatform.Server.Models.wics.Broadcast item);
        partial void OnAfterBroadcastCreated(WicsPlatform.Server.Models.wics.Broadcast item);

        public async Task<WicsPlatform.Server.Models.wics.Broadcast> CreateBroadcast(WicsPlatform.Server.Models.wics.Broadcast broadcast)
        {
            OnBroadcastCreated(broadcast);

            var existingItem = Context.Broadcasts
                              .Where(i => i.Id == broadcast.Id)
                              .FirstOrDefault();

            if (existingItem != null)
            {
               throw new Exception("Item already available");
            }            

            try
            {
                Context.Broadcasts.Add(broadcast);
                Context.SaveChanges();
            }
            catch
            {
                Context.Entry(broadcast).State = EntityState.Detached;
                throw;
            }

            OnAfterBroadcastCreated(broadcast);

            return broadcast;
        }

        public async Task<WicsPlatform.Server.Models.wics.Broadcast> CancelBroadcastChanges(WicsPlatform.Server.Models.wics.Broadcast item)
        {
            var entityToCancel = Context.Entry(item);
            if (entityToCancel.State == EntityState.Modified)
            {
              entityToCancel.CurrentValues.SetValues(entityToCancel.OriginalValues);
              entityToCancel.State = EntityState.Unchanged;
            }

            return item;
        }

        partial void OnBroadcastUpdated(WicsPlatform.Server.Models.wics.Broadcast item);
        partial void OnAfterBroadcastUpdated(WicsPlatform.Server.Models.wics.Broadcast item);

        public async Task<WicsPlatform.Server.Models.wics.Broadcast> UpdateBroadcast(ulong id, WicsPlatform.Server.Models.wics.Broadcast broadcast)
        {
            OnBroadcastUpdated(broadcast);

            var itemToUpdate = Context.Broadcasts
                              .Where(i => i.Id == broadcast.Id)
                              .FirstOrDefault();

            if (itemToUpdate == null)
            {
               throw new Exception("Item no longer available");
            }
                
            var entryToUpdate = Context.Entry(itemToUpdate);
            entryToUpdate.CurrentValues.SetValues(broadcast);
            entryToUpdate.State = EntityState.Modified;

            Context.SaveChanges();

            OnAfterBroadcastUpdated(broadcast);

            return broadcast;
        }

        partial void OnBroadcastDeleted(WicsPlatform.Server.Models.wics.Broadcast item);
        partial void OnAfterBroadcastDeleted(WicsPlatform.Server.Models.wics.Broadcast item);

        public async Task<WicsPlatform.Server.Models.wics.Broadcast> DeleteBroadcast(ulong id)
        {
            var itemToDelete = Context.Broadcasts
                              .Where(i => i.Id == id)
                              .FirstOrDefault();

            if (itemToDelete == null)
            {
               throw new Exception("Item no longer available");
            }

            OnBroadcastDeleted(itemToDelete);


            Context.Broadcasts.Remove(itemToDelete);

            try
            {
                Context.SaveChanges();
            }
            catch
            {
                Context.Entry(itemToDelete).State = EntityState.Unchanged;
                throw;
            }

            OnAfterBroadcastDeleted(itemToDelete);

            return itemToDelete;
        }
    
        public async Task ExportChannelsToExcel(Query query = null, string fileName = null)
        {
            navigationManager.NavigateTo(query != null ? query.ToUrl($"export/wics/channels/excel(fileName='{(!string.IsNullOrEmpty(fileName) ? UrlEncoder.Default.Encode(fileName) : "Export")}')") : $"export/wics/channels/excel(fileName='{(!string.IsNullOrEmpty(fileName) ? UrlEncoder.Default.Encode(fileName) : "Export")}')", true);
        }

        public async Task ExportChannelsToCSV(Query query = null, string fileName = null)
        {
            navigationManager.NavigateTo(query != null ? query.ToUrl($"export/wics/channels/csv(fileName='{(!string.IsNullOrEmpty(fileName) ? UrlEncoder.Default.Encode(fileName) : "Export")}')") : $"export/wics/channels/csv(fileName='{(!string.IsNullOrEmpty(fileName) ? UrlEncoder.Default.Encode(fileName) : "Export")}')", true);
        }

        partial void OnChannelsRead(ref IQueryable<WicsPlatform.Server.Models.wics.Channel> items);

        public async Task<IQueryable<WicsPlatform.Server.Models.wics.Channel>> GetChannels(Query query = null)
        {
            var items = Context.Channels.AsQueryable();


            if (query != null)
            {
                if (!string.IsNullOrEmpty(query.Expand))
                {
                    var propertiesToExpand = query.Expand.Split(',');
                    foreach(var p in propertiesToExpand)
                    {
                        items = items.Include(p.Trim());
                    }
                }

                ApplyQuery(ref items, query);
            }

            OnChannelsRead(ref items);

            return await Task.FromResult(items);
        }

        partial void OnChannelGet(WicsPlatform.Server.Models.wics.Channel item);
        partial void OnGetChannelById(ref IQueryable<WicsPlatform.Server.Models.wics.Channel> items);


        public async Task<WicsPlatform.Server.Models.wics.Channel> GetChannelById(ulong id)
        {
            var items = Context.Channels
                              .AsNoTracking()
                              .Where(i => i.Id == id);

 
            OnGetChannelById(ref items);

            var itemToReturn = items.FirstOrDefault();

            OnChannelGet(itemToReturn);

            return await Task.FromResult(itemToReturn);
        }

        partial void OnChannelCreated(WicsPlatform.Server.Models.wics.Channel item);
        partial void OnAfterChannelCreated(WicsPlatform.Server.Models.wics.Channel item);

        public async Task<WicsPlatform.Server.Models.wics.Channel> CreateChannel(WicsPlatform.Server.Models.wics.Channel channel)
        {
            OnChannelCreated(channel);

            var existingItem = Context.Channels
                              .Where(i => i.Id == channel.Id)
                              .FirstOrDefault();

            if (existingItem != null)
            {
               throw new Exception("Item already available");
            }            

            try
            {
                Context.Channels.Add(channel);
                Context.SaveChanges();
            }
            catch
            {
                Context.Entry(channel).State = EntityState.Detached;
                throw;
            }

            OnAfterChannelCreated(channel);

            return channel;
        }

        public async Task<WicsPlatform.Server.Models.wics.Channel> CancelChannelChanges(WicsPlatform.Server.Models.wics.Channel item)
        {
            var entityToCancel = Context.Entry(item);
            if (entityToCancel.State == EntityState.Modified)
            {
              entityToCancel.CurrentValues.SetValues(entityToCancel.OriginalValues);
              entityToCancel.State = EntityState.Unchanged;
            }

            return item;
        }

        partial void OnChannelUpdated(WicsPlatform.Server.Models.wics.Channel item);
        partial void OnAfterChannelUpdated(WicsPlatform.Server.Models.wics.Channel item);

        public async Task<WicsPlatform.Server.Models.wics.Channel> UpdateChannel(ulong id, WicsPlatform.Server.Models.wics.Channel channel)
        {
            OnChannelUpdated(channel);

            var itemToUpdate = Context.Channels
                              .Where(i => i.Id == channel.Id)
                              .FirstOrDefault();

            if (itemToUpdate == null)
            {
               throw new Exception("Item no longer available");
            }
                
            var entryToUpdate = Context.Entry(itemToUpdate);
            entryToUpdate.CurrentValues.SetValues(channel);
            entryToUpdate.State = EntityState.Modified;

            Context.SaveChanges();

            OnAfterChannelUpdated(channel);

            return channel;
        }

        partial void OnChannelDeleted(WicsPlatform.Server.Models.wics.Channel item);
        partial void OnAfterChannelDeleted(WicsPlatform.Server.Models.wics.Channel item);

        public async Task<WicsPlatform.Server.Models.wics.Channel> DeleteChannel(ulong id)
        {
            var itemToDelete = Context.Channels
                              .Where(i => i.Id == id)
                              .Include(i => i.Broadcasts)
                              .Include(i => i.MapChannelMedia)
                              .Include(i => i.MapChannelTts)
                              .FirstOrDefault();

            if (itemToDelete == null)
            {
               throw new Exception("Item no longer available");
            }

            OnChannelDeleted(itemToDelete);


            Context.Channels.Remove(itemToDelete);

            try
            {
                Context.SaveChanges();
            }
            catch
            {
                Context.Entry(itemToDelete).State = EntityState.Unchanged;
                throw;
            }

            OnAfterChannelDeleted(itemToDelete);

            return itemToDelete;
        }
    
        public async Task ExportGroupsToExcel(Query query = null, string fileName = null)
        {
            navigationManager.NavigateTo(query != null ? query.ToUrl($"export/wics/groups/excel(fileName='{(!string.IsNullOrEmpty(fileName) ? UrlEncoder.Default.Encode(fileName) : "Export")}')") : $"export/wics/groups/excel(fileName='{(!string.IsNullOrEmpty(fileName) ? UrlEncoder.Default.Encode(fileName) : "Export")}')", true);
        }

        public async Task ExportGroupsToCSV(Query query = null, string fileName = null)
        {
            navigationManager.NavigateTo(query != null ? query.ToUrl($"export/wics/groups/csv(fileName='{(!string.IsNullOrEmpty(fileName) ? UrlEncoder.Default.Encode(fileName) : "Export")}')") : $"export/wics/groups/csv(fileName='{(!string.IsNullOrEmpty(fileName) ? UrlEncoder.Default.Encode(fileName) : "Export")}')", true);
        }

        partial void OnGroupsRead(ref IQueryable<WicsPlatform.Server.Models.wics.Group> items);

        public async Task<IQueryable<WicsPlatform.Server.Models.wics.Group>> GetGroups(Query query = null)
        {
            var items = Context.Groups.AsQueryable();


            if (query != null)
            {
                if (!string.IsNullOrEmpty(query.Expand))
                {
                    var propertiesToExpand = query.Expand.Split(',');
                    foreach(var p in propertiesToExpand)
                    {
                        items = items.Include(p.Trim());
                    }
                }

                ApplyQuery(ref items, query);
            }

            OnGroupsRead(ref items);

            return await Task.FromResult(items);
        }

        partial void OnGroupGet(WicsPlatform.Server.Models.wics.Group item);
        partial void OnGetGroupById(ref IQueryable<WicsPlatform.Server.Models.wics.Group> items);


        public async Task<WicsPlatform.Server.Models.wics.Group> GetGroupById(ulong id)
        {
            var items = Context.Groups
                              .AsNoTracking()
                              .Where(i => i.Id == id);

 
            OnGetGroupById(ref items);

            var itemToReturn = items.FirstOrDefault();

            OnGroupGet(itemToReturn);

            return await Task.FromResult(itemToReturn);
        }

        partial void OnGroupCreated(WicsPlatform.Server.Models.wics.Group item);
        partial void OnAfterGroupCreated(WicsPlatform.Server.Models.wics.Group item);

        public async Task<WicsPlatform.Server.Models.wics.Group> CreateGroup(WicsPlatform.Server.Models.wics.Group _group)
        {
            OnGroupCreated(_group);

            var existingItem = Context.Groups
                              .Where(i => i.Id == _group.Id)
                              .FirstOrDefault();

            if (existingItem != null)
            {
               throw new Exception("Item already available");
            }            

            try
            {
                Context.Groups.Add(_group);
                Context.SaveChanges();
            }
            catch
            {
                Context.Entry(_group).State = EntityState.Detached;
                throw;
            }

            OnAfterGroupCreated(_group);

            return _group;
        }

        public async Task<WicsPlatform.Server.Models.wics.Group> CancelGroupChanges(WicsPlatform.Server.Models.wics.Group item)
        {
            var entityToCancel = Context.Entry(item);
            if (entityToCancel.State == EntityState.Modified)
            {
              entityToCancel.CurrentValues.SetValues(entityToCancel.OriginalValues);
              entityToCancel.State = EntityState.Unchanged;
            }

            return item;
        }

        partial void OnGroupUpdated(WicsPlatform.Server.Models.wics.Group item);
        partial void OnAfterGroupUpdated(WicsPlatform.Server.Models.wics.Group item);

        public async Task<WicsPlatform.Server.Models.wics.Group> UpdateGroup(ulong id, WicsPlatform.Server.Models.wics.Group _group)
        {
            OnGroupUpdated(_group);

            var itemToUpdate = Context.Groups
                              .Where(i => i.Id == _group.Id)
                              .FirstOrDefault();

            if (itemToUpdate == null)
            {
               throw new Exception("Item no longer available");
            }
                
            var entryToUpdate = Context.Entry(itemToUpdate);
            entryToUpdate.CurrentValues.SetValues(_group);
            entryToUpdate.State = EntityState.Modified;

            Context.SaveChanges();

            OnAfterGroupUpdated(_group);

            return _group;
        }

        partial void OnGroupDeleted(WicsPlatform.Server.Models.wics.Group item);
        partial void OnAfterGroupDeleted(WicsPlatform.Server.Models.wics.Group item);

        public async Task<WicsPlatform.Server.Models.wics.Group> DeleteGroup(ulong id)
        {
            var itemToDelete = Context.Groups
                              .Where(i => i.Id == id)
                              .Include(i => i.MapMediaGroups)
                              .Include(i => i.MapSpeakerGroups)
                              .FirstOrDefault();

            if (itemToDelete == null)
            {
               throw new Exception("Item no longer available");
            }

            OnGroupDeleted(itemToDelete);


            Context.Groups.Remove(itemToDelete);

            try
            {
                Context.SaveChanges();
            }
            catch
            {
                Context.Entry(itemToDelete).State = EntityState.Unchanged;
                throw;
            }

            OnAfterGroupDeleted(itemToDelete);

            return itemToDelete;
        }
    
        public async Task ExportMapChannelMediaToExcel(Query query = null, string fileName = null)
        {
            navigationManager.NavigateTo(query != null ? query.ToUrl($"export/wics/mapchannelmedia/excel(fileName='{(!string.IsNullOrEmpty(fileName) ? UrlEncoder.Default.Encode(fileName) : "Export")}')") : $"export/wics/mapchannelmedia/excel(fileName='{(!string.IsNullOrEmpty(fileName) ? UrlEncoder.Default.Encode(fileName) : "Export")}')", true);
        }

        public async Task ExportMapChannelMediaToCSV(Query query = null, string fileName = null)
        {
            navigationManager.NavigateTo(query != null ? query.ToUrl($"export/wics/mapchannelmedia/csv(fileName='{(!string.IsNullOrEmpty(fileName) ? UrlEncoder.Default.Encode(fileName) : "Export")}')") : $"export/wics/mapchannelmedia/csv(fileName='{(!string.IsNullOrEmpty(fileName) ? UrlEncoder.Default.Encode(fileName) : "Export")}')", true);
        }

        partial void OnMapChannelMediaRead(ref IQueryable<WicsPlatform.Server.Models.wics.MapChannelMedium> items);

        public async Task<IQueryable<WicsPlatform.Server.Models.wics.MapChannelMedium>> GetMapChannelMedia(Query query = null)
        {
            var items = Context.MapChannelMedia.AsQueryable();

            items = items.Include(i => i.Channel);
            items = items.Include(i => i.Medium);

            if (query != null)
            {
                if (!string.IsNullOrEmpty(query.Expand))
                {
                    var propertiesToExpand = query.Expand.Split(',');
                    foreach(var p in propertiesToExpand)
                    {
                        items = items.Include(p.Trim());
                    }
                }

                ApplyQuery(ref items, query);
            }

            OnMapChannelMediaRead(ref items);

            return await Task.FromResult(items);
        }

        partial void OnMapChannelMediumGet(WicsPlatform.Server.Models.wics.MapChannelMedium item);
        partial void OnGetMapChannelMediumById(ref IQueryable<WicsPlatform.Server.Models.wics.MapChannelMedium> items);


        public async Task<WicsPlatform.Server.Models.wics.MapChannelMedium> GetMapChannelMediumById(ulong id)
        {
            var items = Context.MapChannelMedia
                              .AsNoTracking()
                              .Where(i => i.Id == id);

            items = items.Include(i => i.Channel);
            items = items.Include(i => i.Medium);
 
            OnGetMapChannelMediumById(ref items);

            var itemToReturn = items.FirstOrDefault();

            OnMapChannelMediumGet(itemToReturn);

            return await Task.FromResult(itemToReturn);
        }

        partial void OnMapChannelMediumCreated(WicsPlatform.Server.Models.wics.MapChannelMedium item);
        partial void OnAfterMapChannelMediumCreated(WicsPlatform.Server.Models.wics.MapChannelMedium item);

        public async Task<WicsPlatform.Server.Models.wics.MapChannelMedium> CreateMapChannelMedium(WicsPlatform.Server.Models.wics.MapChannelMedium mapchannelmedium)
        {
            OnMapChannelMediumCreated(mapchannelmedium);

            var existingItem = Context.MapChannelMedia
                              .Where(i => i.Id == mapchannelmedium.Id)
                              .FirstOrDefault();

            if (existingItem != null)
            {
               throw new Exception("Item already available");
            }            

            try
            {
                Context.MapChannelMedia.Add(mapchannelmedium);
                Context.SaveChanges();
            }
            catch
            {
                Context.Entry(mapchannelmedium).State = EntityState.Detached;
                throw;
            }

            OnAfterMapChannelMediumCreated(mapchannelmedium);

            return mapchannelmedium;
        }

        public async Task<WicsPlatform.Server.Models.wics.MapChannelMedium> CancelMapChannelMediumChanges(WicsPlatform.Server.Models.wics.MapChannelMedium item)
        {
            var entityToCancel = Context.Entry(item);
            if (entityToCancel.State == EntityState.Modified)
            {
              entityToCancel.CurrentValues.SetValues(entityToCancel.OriginalValues);
              entityToCancel.State = EntityState.Unchanged;
            }

            return item;
        }

        partial void OnMapChannelMediumUpdated(WicsPlatform.Server.Models.wics.MapChannelMedium item);
        partial void OnAfterMapChannelMediumUpdated(WicsPlatform.Server.Models.wics.MapChannelMedium item);

        public async Task<WicsPlatform.Server.Models.wics.MapChannelMedium> UpdateMapChannelMedium(ulong id, WicsPlatform.Server.Models.wics.MapChannelMedium mapchannelmedium)
        {
            OnMapChannelMediumUpdated(mapchannelmedium);

            var itemToUpdate = Context.MapChannelMedia
                              .Where(i => i.Id == mapchannelmedium.Id)
                              .FirstOrDefault();

            if (itemToUpdate == null)
            {
               throw new Exception("Item no longer available");
            }
                
            var entryToUpdate = Context.Entry(itemToUpdate);
            entryToUpdate.CurrentValues.SetValues(mapchannelmedium);
            entryToUpdate.State = EntityState.Modified;

            Context.SaveChanges();

            OnAfterMapChannelMediumUpdated(mapchannelmedium);

            return mapchannelmedium;
        }

        partial void OnMapChannelMediumDeleted(WicsPlatform.Server.Models.wics.MapChannelMedium item);
        partial void OnAfterMapChannelMediumDeleted(WicsPlatform.Server.Models.wics.MapChannelMedium item);

        public async Task<WicsPlatform.Server.Models.wics.MapChannelMedium> DeleteMapChannelMedium(ulong id)
        {
            var itemToDelete = Context.MapChannelMedia
                              .Where(i => i.Id == id)
                              .FirstOrDefault();

            if (itemToDelete == null)
            {
               throw new Exception("Item no longer available");
            }

            OnMapChannelMediumDeleted(itemToDelete);


            Context.MapChannelMedia.Remove(itemToDelete);

            try
            {
                Context.SaveChanges();
            }
            catch
            {
                Context.Entry(itemToDelete).State = EntityState.Unchanged;
                throw;
            }

            OnAfterMapChannelMediumDeleted(itemToDelete);

            return itemToDelete;
        }
    
        public async Task ExportMapChannelTtsToExcel(Query query = null, string fileName = null)
        {
            navigationManager.NavigateTo(query != null ? query.ToUrl($"export/wics/mapchanneltts/excel(fileName='{(!string.IsNullOrEmpty(fileName) ? UrlEncoder.Default.Encode(fileName) : "Export")}')") : $"export/wics/mapchanneltts/excel(fileName='{(!string.IsNullOrEmpty(fileName) ? UrlEncoder.Default.Encode(fileName) : "Export")}')", true);
        }

        public async Task ExportMapChannelTtsToCSV(Query query = null, string fileName = null)
        {
            navigationManager.NavigateTo(query != null ? query.ToUrl($"export/wics/mapchanneltts/csv(fileName='{(!string.IsNullOrEmpty(fileName) ? UrlEncoder.Default.Encode(fileName) : "Export")}')") : $"export/wics/mapchanneltts/csv(fileName='{(!string.IsNullOrEmpty(fileName) ? UrlEncoder.Default.Encode(fileName) : "Export")}')", true);
        }

        partial void OnMapChannelTtsRead(ref IQueryable<WicsPlatform.Server.Models.wics.MapChannelTt> items);

        public async Task<IQueryable<WicsPlatform.Server.Models.wics.MapChannelTt>> GetMapChannelTts(Query query = null)
        {
            var items = Context.MapChannelTts.AsQueryable();

            items = items.Include(i => i.Channel);
            items = items.Include(i => i.Tt);

            if (query != null)
            {
                if (!string.IsNullOrEmpty(query.Expand))
                {
                    var propertiesToExpand = query.Expand.Split(',');
                    foreach(var p in propertiesToExpand)
                    {
                        items = items.Include(p.Trim());
                    }
                }

                ApplyQuery(ref items, query);
            }

            OnMapChannelTtsRead(ref items);

            return await Task.FromResult(items);
        }

        partial void OnMapChannelTtGet(WicsPlatform.Server.Models.wics.MapChannelTt item);
        partial void OnGetMapChannelTtById(ref IQueryable<WicsPlatform.Server.Models.wics.MapChannelTt> items);


        public async Task<WicsPlatform.Server.Models.wics.MapChannelTt> GetMapChannelTtById(ulong id)
        {
            var items = Context.MapChannelTts
                              .AsNoTracking()
                              .Where(i => i.Id == id);

            items = items.Include(i => i.Channel);
            items = items.Include(i => i.Tt);
 
            OnGetMapChannelTtById(ref items);

            var itemToReturn = items.FirstOrDefault();

            OnMapChannelTtGet(itemToReturn);

            return await Task.FromResult(itemToReturn);
        }

        partial void OnMapChannelTtCreated(WicsPlatform.Server.Models.wics.MapChannelTt item);
        partial void OnAfterMapChannelTtCreated(WicsPlatform.Server.Models.wics.MapChannelTt item);

        public async Task<WicsPlatform.Server.Models.wics.MapChannelTt> CreateMapChannelTt(WicsPlatform.Server.Models.wics.MapChannelTt mapchanneltt)
        {
            OnMapChannelTtCreated(mapchanneltt);

            var existingItem = Context.MapChannelTts
                              .Where(i => i.Id == mapchanneltt.Id)
                              .FirstOrDefault();

            if (existingItem != null)
            {
               throw new Exception("Item already available");
            }            

            try
            {
                Context.MapChannelTts.Add(mapchanneltt);
                Context.SaveChanges();
            }
            catch
            {
                Context.Entry(mapchanneltt).State = EntityState.Detached;
                throw;
            }

            OnAfterMapChannelTtCreated(mapchanneltt);

            return mapchanneltt;
        }

        public async Task<WicsPlatform.Server.Models.wics.MapChannelTt> CancelMapChannelTtChanges(WicsPlatform.Server.Models.wics.MapChannelTt item)
        {
            var entityToCancel = Context.Entry(item);
            if (entityToCancel.State == EntityState.Modified)
            {
              entityToCancel.CurrentValues.SetValues(entityToCancel.OriginalValues);
              entityToCancel.State = EntityState.Unchanged;
            }

            return item;
        }

        partial void OnMapChannelTtUpdated(WicsPlatform.Server.Models.wics.MapChannelTt item);
        partial void OnAfterMapChannelTtUpdated(WicsPlatform.Server.Models.wics.MapChannelTt item);

        public async Task<WicsPlatform.Server.Models.wics.MapChannelTt> UpdateMapChannelTt(ulong id, WicsPlatform.Server.Models.wics.MapChannelTt mapchanneltt)
        {
            OnMapChannelTtUpdated(mapchanneltt);

            var itemToUpdate = Context.MapChannelTts
                              .Where(i => i.Id == mapchanneltt.Id)
                              .FirstOrDefault();

            if (itemToUpdate == null)
            {
               throw new Exception("Item no longer available");
            }
                
            var entryToUpdate = Context.Entry(itemToUpdate);
            entryToUpdate.CurrentValues.SetValues(mapchanneltt);
            entryToUpdate.State = EntityState.Modified;

            Context.SaveChanges();

            OnAfterMapChannelTtUpdated(mapchanneltt);

            return mapchanneltt;
        }

        partial void OnMapChannelTtDeleted(WicsPlatform.Server.Models.wics.MapChannelTt item);
        partial void OnAfterMapChannelTtDeleted(WicsPlatform.Server.Models.wics.MapChannelTt item);

        public async Task<WicsPlatform.Server.Models.wics.MapChannelTt> DeleteMapChannelTt(ulong id)
        {
            var itemToDelete = Context.MapChannelTts
                              .Where(i => i.Id == id)
                              .FirstOrDefault();

            if (itemToDelete == null)
            {
               throw new Exception("Item no longer available");
            }

            OnMapChannelTtDeleted(itemToDelete);


            Context.MapChannelTts.Remove(itemToDelete);

            try
            {
                Context.SaveChanges();
            }
            catch
            {
                Context.Entry(itemToDelete).State = EntityState.Unchanged;
                throw;
            }

            OnAfterMapChannelTtDeleted(itemToDelete);

            return itemToDelete;
        }
    
        public async Task ExportMapMediaGroupsToExcel(Query query = null, string fileName = null)
        {
            navigationManager.NavigateTo(query != null ? query.ToUrl($"export/wics/mapmediagroups/excel(fileName='{(!string.IsNullOrEmpty(fileName) ? UrlEncoder.Default.Encode(fileName) : "Export")}')") : $"export/wics/mapmediagroups/excel(fileName='{(!string.IsNullOrEmpty(fileName) ? UrlEncoder.Default.Encode(fileName) : "Export")}')", true);
        }

        public async Task ExportMapMediaGroupsToCSV(Query query = null, string fileName = null)
        {
            navigationManager.NavigateTo(query != null ? query.ToUrl($"export/wics/mapmediagroups/csv(fileName='{(!string.IsNullOrEmpty(fileName) ? UrlEncoder.Default.Encode(fileName) : "Export")}')") : $"export/wics/mapmediagroups/csv(fileName='{(!string.IsNullOrEmpty(fileName) ? UrlEncoder.Default.Encode(fileName) : "Export")}')", true);
        }

        partial void OnMapMediaGroupsRead(ref IQueryable<WicsPlatform.Server.Models.wics.MapMediaGroup> items);

        public async Task<IQueryable<WicsPlatform.Server.Models.wics.MapMediaGroup>> GetMapMediaGroups(Query query = null)
        {
            var items = Context.MapMediaGroups.AsQueryable();

            items = items.Include(i => i.Group);
            items = items.Include(i => i.Medium);

            if (query != null)
            {
                if (!string.IsNullOrEmpty(query.Expand))
                {
                    var propertiesToExpand = query.Expand.Split(',');
                    foreach(var p in propertiesToExpand)
                    {
                        items = items.Include(p.Trim());
                    }
                }

                ApplyQuery(ref items, query);
            }

            OnMapMediaGroupsRead(ref items);

            return await Task.FromResult(items);
        }

        partial void OnMapMediaGroupGet(WicsPlatform.Server.Models.wics.MapMediaGroup item);
        partial void OnGetMapMediaGroupById(ref IQueryable<WicsPlatform.Server.Models.wics.MapMediaGroup> items);


        public async Task<WicsPlatform.Server.Models.wics.MapMediaGroup> GetMapMediaGroupById(ulong id)
        {
            var items = Context.MapMediaGroups
                              .AsNoTracking()
                              .Where(i => i.Id == id);

            items = items.Include(i => i.Group);
            items = items.Include(i => i.Medium);
 
            OnGetMapMediaGroupById(ref items);

            var itemToReturn = items.FirstOrDefault();

            OnMapMediaGroupGet(itemToReturn);

            return await Task.FromResult(itemToReturn);
        }

        partial void OnMapMediaGroupCreated(WicsPlatform.Server.Models.wics.MapMediaGroup item);
        partial void OnAfterMapMediaGroupCreated(WicsPlatform.Server.Models.wics.MapMediaGroup item);

        public async Task<WicsPlatform.Server.Models.wics.MapMediaGroup> CreateMapMediaGroup(WicsPlatform.Server.Models.wics.MapMediaGroup mapmediagroup)
        {
            OnMapMediaGroupCreated(mapmediagroup);

            var existingItem = Context.MapMediaGroups
                              .Where(i => i.Id == mapmediagroup.Id)
                              .FirstOrDefault();

            if (existingItem != null)
            {
               throw new Exception("Item already available");
            }            

            try
            {
                Context.MapMediaGroups.Add(mapmediagroup);
                Context.SaveChanges();
            }
            catch
            {
                Context.Entry(mapmediagroup).State = EntityState.Detached;
                throw;
            }

            OnAfterMapMediaGroupCreated(mapmediagroup);

            return mapmediagroup;
        }

        public async Task<WicsPlatform.Server.Models.wics.MapMediaGroup> CancelMapMediaGroupChanges(WicsPlatform.Server.Models.wics.MapMediaGroup item)
        {
            var entityToCancel = Context.Entry(item);
            if (entityToCancel.State == EntityState.Modified)
            {
              entityToCancel.CurrentValues.SetValues(entityToCancel.OriginalValues);
              entityToCancel.State = EntityState.Unchanged;
            }

            return item;
        }

        partial void OnMapMediaGroupUpdated(WicsPlatform.Server.Models.wics.MapMediaGroup item);
        partial void OnAfterMapMediaGroupUpdated(WicsPlatform.Server.Models.wics.MapMediaGroup item);

        public async Task<WicsPlatform.Server.Models.wics.MapMediaGroup> UpdateMapMediaGroup(ulong id, WicsPlatform.Server.Models.wics.MapMediaGroup mapmediagroup)
        {
            OnMapMediaGroupUpdated(mapmediagroup);

            var itemToUpdate = Context.MapMediaGroups
                              .Where(i => i.Id == mapmediagroup.Id)
                              .FirstOrDefault();

            if (itemToUpdate == null)
            {
               throw new Exception("Item no longer available");
            }
                
            var entryToUpdate = Context.Entry(itemToUpdate);
            entryToUpdate.CurrentValues.SetValues(mapmediagroup);
            entryToUpdate.State = EntityState.Modified;

            Context.SaveChanges();

            OnAfterMapMediaGroupUpdated(mapmediagroup);

            return mapmediagroup;
        }

        partial void OnMapMediaGroupDeleted(WicsPlatform.Server.Models.wics.MapMediaGroup item);
        partial void OnAfterMapMediaGroupDeleted(WicsPlatform.Server.Models.wics.MapMediaGroup item);

        public async Task<WicsPlatform.Server.Models.wics.MapMediaGroup> DeleteMapMediaGroup(ulong id)
        {
            var itemToDelete = Context.MapMediaGroups
                              .Where(i => i.Id == id)
                              .FirstOrDefault();

            if (itemToDelete == null)
            {
               throw new Exception("Item no longer available");
            }

            OnMapMediaGroupDeleted(itemToDelete);


            Context.MapMediaGroups.Remove(itemToDelete);

            try
            {
                Context.SaveChanges();
            }
            catch
            {
                Context.Entry(itemToDelete).State = EntityState.Unchanged;
                throw;
            }

            OnAfterMapMediaGroupDeleted(itemToDelete);

            return itemToDelete;
        }
    
        public async Task ExportMapSpeakerGroupsToExcel(Query query = null, string fileName = null)
        {
            navigationManager.NavigateTo(query != null ? query.ToUrl($"export/wics/mapspeakergroups/excel(fileName='{(!string.IsNullOrEmpty(fileName) ? UrlEncoder.Default.Encode(fileName) : "Export")}')") : $"export/wics/mapspeakergroups/excel(fileName='{(!string.IsNullOrEmpty(fileName) ? UrlEncoder.Default.Encode(fileName) : "Export")}')", true);
        }

        public async Task ExportMapSpeakerGroupsToCSV(Query query = null, string fileName = null)
        {
            navigationManager.NavigateTo(query != null ? query.ToUrl($"export/wics/mapspeakergroups/csv(fileName='{(!string.IsNullOrEmpty(fileName) ? UrlEncoder.Default.Encode(fileName) : "Export")}')") : $"export/wics/mapspeakergroups/csv(fileName='{(!string.IsNullOrEmpty(fileName) ? UrlEncoder.Default.Encode(fileName) : "Export")}')", true);
        }

        partial void OnMapSpeakerGroupsRead(ref IQueryable<WicsPlatform.Server.Models.wics.MapSpeakerGroup> items);

        public async Task<IQueryable<WicsPlatform.Server.Models.wics.MapSpeakerGroup>> GetMapSpeakerGroups(Query query = null)
        {
            var items = Context.MapSpeakerGroups.AsQueryable();

            items = items.Include(i => i.Group);
            items = items.Include(i => i.Speaker);

            if (query != null)
            {
                if (!string.IsNullOrEmpty(query.Expand))
                {
                    var propertiesToExpand = query.Expand.Split(',');
                    foreach(var p in propertiesToExpand)
                    {
                        items = items.Include(p.Trim());
                    }
                }

                ApplyQuery(ref items, query);
            }

            OnMapSpeakerGroupsRead(ref items);

            return await Task.FromResult(items);
        }

        partial void OnMapSpeakerGroupGet(WicsPlatform.Server.Models.wics.MapSpeakerGroup item);
        partial void OnGetMapSpeakerGroupById(ref IQueryable<WicsPlatform.Server.Models.wics.MapSpeakerGroup> items);


        public async Task<WicsPlatform.Server.Models.wics.MapSpeakerGroup> GetMapSpeakerGroupById(ulong id)
        {
            var items = Context.MapSpeakerGroups
                              .AsNoTracking()
                              .Where(i => i.Id == id);

            items = items.Include(i => i.Group);
            items = items.Include(i => i.Speaker);
 
            OnGetMapSpeakerGroupById(ref items);

            var itemToReturn = items.FirstOrDefault();

            OnMapSpeakerGroupGet(itemToReturn);

            return await Task.FromResult(itemToReturn);
        }

        partial void OnMapSpeakerGroupCreated(WicsPlatform.Server.Models.wics.MapSpeakerGroup item);
        partial void OnAfterMapSpeakerGroupCreated(WicsPlatform.Server.Models.wics.MapSpeakerGroup item);

        public async Task<WicsPlatform.Server.Models.wics.MapSpeakerGroup> CreateMapSpeakerGroup(WicsPlatform.Server.Models.wics.MapSpeakerGroup mapspeakergroup)
        {
            OnMapSpeakerGroupCreated(mapspeakergroup);

            var existingItem = Context.MapSpeakerGroups
                              .Where(i => i.Id == mapspeakergroup.Id)
                              .FirstOrDefault();

            if (existingItem != null)
            {
               throw new Exception("Item already available");
            }            

            try
            {
                Context.MapSpeakerGroups.Add(mapspeakergroup);
                Context.SaveChanges();
            }
            catch
            {
                Context.Entry(mapspeakergroup).State = EntityState.Detached;
                throw;
            }

            OnAfterMapSpeakerGroupCreated(mapspeakergroup);

            return mapspeakergroup;
        }

        public async Task<WicsPlatform.Server.Models.wics.MapSpeakerGroup> CancelMapSpeakerGroupChanges(WicsPlatform.Server.Models.wics.MapSpeakerGroup item)
        {
            var entityToCancel = Context.Entry(item);
            if (entityToCancel.State == EntityState.Modified)
            {
              entityToCancel.CurrentValues.SetValues(entityToCancel.OriginalValues);
              entityToCancel.State = EntityState.Unchanged;
            }

            return item;
        }

        partial void OnMapSpeakerGroupUpdated(WicsPlatform.Server.Models.wics.MapSpeakerGroup item);
        partial void OnAfterMapSpeakerGroupUpdated(WicsPlatform.Server.Models.wics.MapSpeakerGroup item);

        public async Task<WicsPlatform.Server.Models.wics.MapSpeakerGroup> UpdateMapSpeakerGroup(ulong id, WicsPlatform.Server.Models.wics.MapSpeakerGroup mapspeakergroup)
        {
            OnMapSpeakerGroupUpdated(mapspeakergroup);

            var itemToUpdate = Context.MapSpeakerGroups
                              .Where(i => i.Id == mapspeakergroup.Id)
                              .FirstOrDefault();

            if (itemToUpdate == null)
            {
               throw new Exception("Item no longer available");
            }
                
            var entryToUpdate = Context.Entry(itemToUpdate);
            entryToUpdate.CurrentValues.SetValues(mapspeakergroup);
            entryToUpdate.State = EntityState.Modified;

            Context.SaveChanges();

            OnAfterMapSpeakerGroupUpdated(mapspeakergroup);

            return mapspeakergroup;
        }

        partial void OnMapSpeakerGroupDeleted(WicsPlatform.Server.Models.wics.MapSpeakerGroup item);
        partial void OnAfterMapSpeakerGroupDeleted(WicsPlatform.Server.Models.wics.MapSpeakerGroup item);

        public async Task<WicsPlatform.Server.Models.wics.MapSpeakerGroup> DeleteMapSpeakerGroup(ulong id)
        {
            var itemToDelete = Context.MapSpeakerGroups
                              .Where(i => i.Id == id)
                              .FirstOrDefault();

            if (itemToDelete == null)
            {
               throw new Exception("Item no longer available");
            }

            OnMapSpeakerGroupDeleted(itemToDelete);


            Context.MapSpeakerGroups.Remove(itemToDelete);

            try
            {
                Context.SaveChanges();
            }
            catch
            {
                Context.Entry(itemToDelete).State = EntityState.Unchanged;
                throw;
            }

            OnAfterMapSpeakerGroupDeleted(itemToDelete);

            return itemToDelete;
        }
    
        public async Task ExportMediaToExcel(Query query = null, string fileName = null)
        {
            navigationManager.NavigateTo(query != null ? query.ToUrl($"export/wics/media/excel(fileName='{(!string.IsNullOrEmpty(fileName) ? UrlEncoder.Default.Encode(fileName) : "Export")}')") : $"export/wics/media/excel(fileName='{(!string.IsNullOrEmpty(fileName) ? UrlEncoder.Default.Encode(fileName) : "Export")}')", true);
        }

        public async Task ExportMediaToCSV(Query query = null, string fileName = null)
        {
            navigationManager.NavigateTo(query != null ? query.ToUrl($"export/wics/media/csv(fileName='{(!string.IsNullOrEmpty(fileName) ? UrlEncoder.Default.Encode(fileName) : "Export")}')") : $"export/wics/media/csv(fileName='{(!string.IsNullOrEmpty(fileName) ? UrlEncoder.Default.Encode(fileName) : "Export")}')", true);
        }

        partial void OnMediaRead(ref IQueryable<WicsPlatform.Server.Models.wics.Medium> items);

        public async Task<IQueryable<WicsPlatform.Server.Models.wics.Medium>> GetMedia(Query query = null)
        {
            var items = Context.Media.AsQueryable();


            if (query != null)
            {
                if (!string.IsNullOrEmpty(query.Expand))
                {
                    var propertiesToExpand = query.Expand.Split(',');
                    foreach(var p in propertiesToExpand)
                    {
                        items = items.Include(p.Trim());
                    }
                }

                ApplyQuery(ref items, query);
            }

            OnMediaRead(ref items);

            return await Task.FromResult(items);
        }

        partial void OnMediumGet(WicsPlatform.Server.Models.wics.Medium item);
        partial void OnGetMediumById(ref IQueryable<WicsPlatform.Server.Models.wics.Medium> items);


        public async Task<WicsPlatform.Server.Models.wics.Medium> GetMediumById(ulong id)
        {
            var items = Context.Media
                              .AsNoTracking()
                              .Where(i => i.Id == id);

 
            OnGetMediumById(ref items);

            var itemToReturn = items.FirstOrDefault();

            OnMediumGet(itemToReturn);

            return await Task.FromResult(itemToReturn);
        }

        partial void OnMediumCreated(WicsPlatform.Server.Models.wics.Medium item);
        partial void OnAfterMediumCreated(WicsPlatform.Server.Models.wics.Medium item);

        public async Task<WicsPlatform.Server.Models.wics.Medium> CreateMedium(WicsPlatform.Server.Models.wics.Medium medium)
        {
            OnMediumCreated(medium);

            var existingItem = Context.Media
                              .Where(i => i.Id == medium.Id)
                              .FirstOrDefault();

            if (existingItem != null)
            {
               throw new Exception("Item already available");
            }            

            try
            {
                Context.Media.Add(medium);
                Context.SaveChanges();
            }
            catch
            {
                Context.Entry(medium).State = EntityState.Detached;
                throw;
            }

            OnAfterMediumCreated(medium);

            return medium;
        }

        public async Task<WicsPlatform.Server.Models.wics.Medium> CancelMediumChanges(WicsPlatform.Server.Models.wics.Medium item)
        {
            var entityToCancel = Context.Entry(item);
            if (entityToCancel.State == EntityState.Modified)
            {
              entityToCancel.CurrentValues.SetValues(entityToCancel.OriginalValues);
              entityToCancel.State = EntityState.Unchanged;
            }

            return item;
        }

        partial void OnMediumUpdated(WicsPlatform.Server.Models.wics.Medium item);
        partial void OnAfterMediumUpdated(WicsPlatform.Server.Models.wics.Medium item);

        public async Task<WicsPlatform.Server.Models.wics.Medium> UpdateMedium(ulong id, WicsPlatform.Server.Models.wics.Medium medium)
        {
            OnMediumUpdated(medium);

            var itemToUpdate = Context.Media
                              .Where(i => i.Id == medium.Id)
                              .FirstOrDefault();

            if (itemToUpdate == null)
            {
               throw new Exception("Item no longer available");
            }
                
            var entryToUpdate = Context.Entry(itemToUpdate);
            entryToUpdate.CurrentValues.SetValues(medium);
            entryToUpdate.State = EntityState.Modified;

            Context.SaveChanges();

            OnAfterMediumUpdated(medium);

            return medium;
        }

        partial void OnMediumDeleted(WicsPlatform.Server.Models.wics.Medium item);
        partial void OnAfterMediumDeleted(WicsPlatform.Server.Models.wics.Medium item);

        public async Task<WicsPlatform.Server.Models.wics.Medium> DeleteMedium(ulong id)
        {
            var itemToDelete = Context.Media
                              .Where(i => i.Id == id)
                              .Include(i => i.MapChannelMedia)
                              .Include(i => i.MapMediaGroups)
                              .FirstOrDefault();

            if (itemToDelete == null)
            {
               throw new Exception("Item no longer available");
            }

            OnMediumDeleted(itemToDelete);


            Context.Media.Remove(itemToDelete);

            try
            {
                Context.SaveChanges();
            }
            catch
            {
                Context.Entry(itemToDelete).State = EntityState.Unchanged;
                throw;
            }

            OnAfterMediumDeleted(itemToDelete);

            return itemToDelete;
        }
    
        public async Task ExportMicsToExcel(Query query = null, string fileName = null)
        {
            navigationManager.NavigateTo(query != null ? query.ToUrl($"export/wics/mics/excel(fileName='{(!string.IsNullOrEmpty(fileName) ? UrlEncoder.Default.Encode(fileName) : "Export")}')") : $"export/wics/mics/excel(fileName='{(!string.IsNullOrEmpty(fileName) ? UrlEncoder.Default.Encode(fileName) : "Export")}')", true);
        }

        public async Task ExportMicsToCSV(Query query = null, string fileName = null)
        {
            navigationManager.NavigateTo(query != null ? query.ToUrl($"export/wics/mics/csv(fileName='{(!string.IsNullOrEmpty(fileName) ? UrlEncoder.Default.Encode(fileName) : "Export")}')") : $"export/wics/mics/csv(fileName='{(!string.IsNullOrEmpty(fileName) ? UrlEncoder.Default.Encode(fileName) : "Export")}')", true);
        }

        partial void OnMicsRead(ref IQueryable<WicsPlatform.Server.Models.wics.Mic> items);

        public async Task<IQueryable<WicsPlatform.Server.Models.wics.Mic>> GetMics(Query query = null)
        {
            var items = Context.Mics.AsQueryable();


            if (query != null)
            {
                if (!string.IsNullOrEmpty(query.Expand))
                {
                    var propertiesToExpand = query.Expand.Split(',');
                    foreach(var p in propertiesToExpand)
                    {
                        items = items.Include(p.Trim());
                    }
                }

                ApplyQuery(ref items, query);
            }

            OnMicsRead(ref items);

            return await Task.FromResult(items);
        }

        partial void OnMicGet(WicsPlatform.Server.Models.wics.Mic item);
        partial void OnGetMicById(ref IQueryable<WicsPlatform.Server.Models.wics.Mic> items);


        public async Task<WicsPlatform.Server.Models.wics.Mic> GetMicById(ulong id)
        {
            var items = Context.Mics
                              .AsNoTracking()
                              .Where(i => i.Id == id);

 
            OnGetMicById(ref items);

            var itemToReturn = items.FirstOrDefault();

            OnMicGet(itemToReturn);

            return await Task.FromResult(itemToReturn);
        }

        partial void OnMicCreated(WicsPlatform.Server.Models.wics.Mic item);
        partial void OnAfterMicCreated(WicsPlatform.Server.Models.wics.Mic item);

        public async Task<WicsPlatform.Server.Models.wics.Mic> CreateMic(WicsPlatform.Server.Models.wics.Mic mic)
        {
            OnMicCreated(mic);

            var existingItem = Context.Mics
                              .Where(i => i.Id == mic.Id)
                              .FirstOrDefault();

            if (existingItem != null)
            {
               throw new Exception("Item already available");
            }            

            try
            {
                Context.Mics.Add(mic);
                Context.SaveChanges();
            }
            catch
            {
                Context.Entry(mic).State = EntityState.Detached;
                throw;
            }

            OnAfterMicCreated(mic);

            return mic;
        }

        public async Task<WicsPlatform.Server.Models.wics.Mic> CancelMicChanges(WicsPlatform.Server.Models.wics.Mic item)
        {
            var entityToCancel = Context.Entry(item);
            if (entityToCancel.State == EntityState.Modified)
            {
              entityToCancel.CurrentValues.SetValues(entityToCancel.OriginalValues);
              entityToCancel.State = EntityState.Unchanged;
            }

            return item;
        }

        partial void OnMicUpdated(WicsPlatform.Server.Models.wics.Mic item);
        partial void OnAfterMicUpdated(WicsPlatform.Server.Models.wics.Mic item);

        public async Task<WicsPlatform.Server.Models.wics.Mic> UpdateMic(ulong id, WicsPlatform.Server.Models.wics.Mic mic)
        {
            OnMicUpdated(mic);

            var itemToUpdate = Context.Mics
                              .Where(i => i.Id == mic.Id)
                              .FirstOrDefault();

            if (itemToUpdate == null)
            {
               throw new Exception("Item no longer available");
            }
                
            var entryToUpdate = Context.Entry(itemToUpdate);
            entryToUpdate.CurrentValues.SetValues(mic);
            entryToUpdate.State = EntityState.Modified;

            Context.SaveChanges();

            OnAfterMicUpdated(mic);

            return mic;
        }

        partial void OnMicDeleted(WicsPlatform.Server.Models.wics.Mic item);
        partial void OnAfterMicDeleted(WicsPlatform.Server.Models.wics.Mic item);

        public async Task<WicsPlatform.Server.Models.wics.Mic> DeleteMic(ulong id)
        {
            var itemToDelete = Context.Mics
                              .Where(i => i.Id == id)
                              .FirstOrDefault();

            if (itemToDelete == null)
            {
               throw new Exception("Item no longer available");
            }

            OnMicDeleted(itemToDelete);


            Context.Mics.Remove(itemToDelete);

            try
            {
                Context.SaveChanges();
            }
            catch
            {
                Context.Entry(itemToDelete).State = EntityState.Unchanged;
                throw;
            }

            OnAfterMicDeleted(itemToDelete);

            return itemToDelete;
        }
    
        public async Task ExportSpeakersToExcel(Query query = null, string fileName = null)
        {
            navigationManager.NavigateTo(query != null ? query.ToUrl($"export/wics/speakers/excel(fileName='{(!string.IsNullOrEmpty(fileName) ? UrlEncoder.Default.Encode(fileName) : "Export")}')") : $"export/wics/speakers/excel(fileName='{(!string.IsNullOrEmpty(fileName) ? UrlEncoder.Default.Encode(fileName) : "Export")}')", true);
        }

        public async Task ExportSpeakersToCSV(Query query = null, string fileName = null)
        {
            navigationManager.NavigateTo(query != null ? query.ToUrl($"export/wics/speakers/csv(fileName='{(!string.IsNullOrEmpty(fileName) ? UrlEncoder.Default.Encode(fileName) : "Export")}')") : $"export/wics/speakers/csv(fileName='{(!string.IsNullOrEmpty(fileName) ? UrlEncoder.Default.Encode(fileName) : "Export")}')", true);
        }

        partial void OnSpeakersRead(ref IQueryable<WicsPlatform.Server.Models.wics.Speaker> items);

        public async Task<IQueryable<WicsPlatform.Server.Models.wics.Speaker>> GetSpeakers(Query query = null)
        {
            var items = Context.Speakers.AsQueryable();


            if (query != null)
            {
                if (!string.IsNullOrEmpty(query.Expand))
                {
                    var propertiesToExpand = query.Expand.Split(',');
                    foreach(var p in propertiesToExpand)
                    {
                        items = items.Include(p.Trim());
                    }
                }

                ApplyQuery(ref items, query);
            }

            OnSpeakersRead(ref items);

            return await Task.FromResult(items);
        }

        partial void OnSpeakerGet(WicsPlatform.Server.Models.wics.Speaker item);
        partial void OnGetSpeakerById(ref IQueryable<WicsPlatform.Server.Models.wics.Speaker> items);


        public async Task<WicsPlatform.Server.Models.wics.Speaker> GetSpeakerById(ulong id)
        {
            var items = Context.Speakers
                              .AsNoTracking()
                              .Where(i => i.Id == id);

 
            OnGetSpeakerById(ref items);

            var itemToReturn = items.FirstOrDefault();

            OnSpeakerGet(itemToReturn);

            return await Task.FromResult(itemToReturn);
        }

        partial void OnSpeakerCreated(WicsPlatform.Server.Models.wics.Speaker item);
        partial void OnAfterSpeakerCreated(WicsPlatform.Server.Models.wics.Speaker item);

        public async Task<WicsPlatform.Server.Models.wics.Speaker> CreateSpeaker(WicsPlatform.Server.Models.wics.Speaker speaker)
        {
            OnSpeakerCreated(speaker);

            var existingItem = Context.Speakers
                              .Where(i => i.Id == speaker.Id)
                              .FirstOrDefault();

            if (existingItem != null)
            {
               throw new Exception("Item already available");
            }            

            try
            {
                Context.Speakers.Add(speaker);
                Context.SaveChanges();
            }
            catch
            {
                Context.Entry(speaker).State = EntityState.Detached;
                throw;
            }

            OnAfterSpeakerCreated(speaker);

            return speaker;
        }

        public async Task<WicsPlatform.Server.Models.wics.Speaker> CancelSpeakerChanges(WicsPlatform.Server.Models.wics.Speaker item)
        {
            var entityToCancel = Context.Entry(item);
            if (entityToCancel.State == EntityState.Modified)
            {
              entityToCancel.CurrentValues.SetValues(entityToCancel.OriginalValues);
              entityToCancel.State = EntityState.Unchanged;
            }

            return item;
        }

        partial void OnSpeakerUpdated(WicsPlatform.Server.Models.wics.Speaker item);
        partial void OnAfterSpeakerUpdated(WicsPlatform.Server.Models.wics.Speaker item);

        public async Task<WicsPlatform.Server.Models.wics.Speaker> UpdateSpeaker(ulong id, WicsPlatform.Server.Models.wics.Speaker speaker)
        {
            OnSpeakerUpdated(speaker);

            var itemToUpdate = Context.Speakers
                              .Where(i => i.Id == speaker.Id)
                              .FirstOrDefault();

            if (itemToUpdate == null)
            {
               throw new Exception("Item no longer available");
            }
                
            var entryToUpdate = Context.Entry(itemToUpdate);
            entryToUpdate.CurrentValues.SetValues(speaker);
            entryToUpdate.State = EntityState.Modified;

            Context.SaveChanges();

            OnAfterSpeakerUpdated(speaker);

            return speaker;
        }

        partial void OnSpeakerDeleted(WicsPlatform.Server.Models.wics.Speaker item);
        partial void OnAfterSpeakerDeleted(WicsPlatform.Server.Models.wics.Speaker item);

        public async Task<WicsPlatform.Server.Models.wics.Speaker> DeleteSpeaker(ulong id)
        {
            var itemToDelete = Context.Speakers
                              .Where(i => i.Id == id)
                              .Include(i => i.MapSpeakerGroups)
                              .FirstOrDefault();

            if (itemToDelete == null)
            {
               throw new Exception("Item no longer available");
            }

            OnSpeakerDeleted(itemToDelete);


            Context.Speakers.Remove(itemToDelete);

            try
            {
                Context.SaveChanges();
            }
            catch
            {
                Context.Entry(itemToDelete).State = EntityState.Unchanged;
                throw;
            }

            OnAfterSpeakerDeleted(itemToDelete);

            return itemToDelete;
        }
    
        public async Task ExportTtsToExcel(Query query = null, string fileName = null)
        {
            navigationManager.NavigateTo(query != null ? query.ToUrl($"export/wics/tts/excel(fileName='{(!string.IsNullOrEmpty(fileName) ? UrlEncoder.Default.Encode(fileName) : "Export")}')") : $"export/wics/tts/excel(fileName='{(!string.IsNullOrEmpty(fileName) ? UrlEncoder.Default.Encode(fileName) : "Export")}')", true);
        }

        public async Task ExportTtsToCSV(Query query = null, string fileName = null)
        {
            navigationManager.NavigateTo(query != null ? query.ToUrl($"export/wics/tts/csv(fileName='{(!string.IsNullOrEmpty(fileName) ? UrlEncoder.Default.Encode(fileName) : "Export")}')") : $"export/wics/tts/csv(fileName='{(!string.IsNullOrEmpty(fileName) ? UrlEncoder.Default.Encode(fileName) : "Export")}')", true);
        }

        partial void OnTtsRead(ref IQueryable<WicsPlatform.Server.Models.wics.Tt> items);

        public async Task<IQueryable<WicsPlatform.Server.Models.wics.Tt>> GetTts(Query query = null)
        {
            var items = Context.Tts.AsQueryable();


            if (query != null)
            {
                if (!string.IsNullOrEmpty(query.Expand))
                {
                    var propertiesToExpand = query.Expand.Split(',');
                    foreach(var p in propertiesToExpand)
                    {
                        items = items.Include(p.Trim());
                    }
                }

                ApplyQuery(ref items, query);
            }

            OnTtsRead(ref items);

            return await Task.FromResult(items);
        }

        partial void OnTtGet(WicsPlatform.Server.Models.wics.Tt item);
        partial void OnGetTtById(ref IQueryable<WicsPlatform.Server.Models.wics.Tt> items);


        public async Task<WicsPlatform.Server.Models.wics.Tt> GetTtById(ulong id)
        {
            var items = Context.Tts
                              .AsNoTracking()
                              .Where(i => i.Id == id);

 
            OnGetTtById(ref items);

            var itemToReturn = items.FirstOrDefault();

            OnTtGet(itemToReturn);

            return await Task.FromResult(itemToReturn);
        }

        partial void OnTtCreated(WicsPlatform.Server.Models.wics.Tt item);
        partial void OnAfterTtCreated(WicsPlatform.Server.Models.wics.Tt item);

        public async Task<WicsPlatform.Server.Models.wics.Tt> CreateTt(WicsPlatform.Server.Models.wics.Tt tt)
        {
            OnTtCreated(tt);

            var existingItem = Context.Tts
                              .Where(i => i.Id == tt.Id)
                              .FirstOrDefault();

            if (existingItem != null)
            {
               throw new Exception("Item already available");
            }            

            try
            {
                Context.Tts.Add(tt);
                Context.SaveChanges();
            }
            catch
            {
                Context.Entry(tt).State = EntityState.Detached;
                throw;
            }

            OnAfterTtCreated(tt);

            return tt;
        }

        public async Task<WicsPlatform.Server.Models.wics.Tt> CancelTtChanges(WicsPlatform.Server.Models.wics.Tt item)
        {
            var entityToCancel = Context.Entry(item);
            if (entityToCancel.State == EntityState.Modified)
            {
              entityToCancel.CurrentValues.SetValues(entityToCancel.OriginalValues);
              entityToCancel.State = EntityState.Unchanged;
            }

            return item;
        }

        partial void OnTtUpdated(WicsPlatform.Server.Models.wics.Tt item);
        partial void OnAfterTtUpdated(WicsPlatform.Server.Models.wics.Tt item);

        public async Task<WicsPlatform.Server.Models.wics.Tt> UpdateTt(ulong id, WicsPlatform.Server.Models.wics.Tt tt)
        {
            OnTtUpdated(tt);

            var itemToUpdate = Context.Tts
                              .Where(i => i.Id == tt.Id)
                              .FirstOrDefault();

            if (itemToUpdate == null)
            {
               throw new Exception("Item no longer available");
            }
                
            var entryToUpdate = Context.Entry(itemToUpdate);
            entryToUpdate.CurrentValues.SetValues(tt);
            entryToUpdate.State = EntityState.Modified;

            Context.SaveChanges();

            OnAfterTtUpdated(tt);

            return tt;
        }

        partial void OnTtDeleted(WicsPlatform.Server.Models.wics.Tt item);
        partial void OnAfterTtDeleted(WicsPlatform.Server.Models.wics.Tt item);

        public async Task<WicsPlatform.Server.Models.wics.Tt> DeleteTt(ulong id)
        {
            var itemToDelete = Context.Tts
                              .Where(i => i.Id == id)
                              .Include(i => i.MapChannelTts)
                              .FirstOrDefault();

            if (itemToDelete == null)
            {
               throw new Exception("Item no longer available");
            }

            OnTtDeleted(itemToDelete);


            Context.Tts.Remove(itemToDelete);

            try
            {
                Context.SaveChanges();
            }
            catch
            {
                Context.Entry(itemToDelete).State = EntityState.Unchanged;
                throw;
            }

            OnAfterTtDeleted(itemToDelete);

            return itemToDelete;
        }
        }
}