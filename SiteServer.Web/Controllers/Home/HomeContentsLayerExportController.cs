﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Web.Http;
using NSwag.Annotations;
using SiteServer.CMS.Core;
using SiteServer.CMS.Core.Office;
using SiteServer.CMS.DataCache;
using SiteServer.CMS.DataCache.Content;
using SiteServer.CMS.ImportExport;
using SiteServer.CMS.Model;
using SiteServer.CMS.Plugin;
using SiteServer.Utils;

namespace SiteServer.API.Controllers.Home
{
    [OpenApiIgnore]
    [RoutePrefix("home/contentsLayerExport")]
    public class HomeContentsLayerExportController : ApiController
    {
        private const string Route = "";

        [HttpGet, Route(Route)]
        public IHttpActionResult GetConfig()
        {
            try
            {
                var request = new AuthenticatedRequest();

                var siteId = request.GetQueryInt("siteId");
                var channelId = request.GetQueryInt("channelId");

                if (!request.IsUserLoggin ||
                    !request.UserPermissionsImpl.HasChannelPermissions(siteId, channelId,
                        ConfigManager.ChannelPermissions.ContentView))
                {
                    return Unauthorized();
                }

                var siteInfo = SiteManager.GetSiteInfo(siteId);
                if (siteInfo == null) return BadRequest("无法确定内容对应的站点");

                var channelInfo = ChannelManager.GetChannelInfo(siteId, channelId);
                if (channelInfo == null) return BadRequest("无法确定内容对应的栏目");

                var columns = ContentManager.GetContentColumns(siteInfo, channelInfo, true);

                var isChecked = CheckManager.GetUserCheckLevel(request.AdminPermissionsImpl, siteInfo, siteId, out var checkedLevel);
                var checkedLevels = CheckManager.GetCheckedLevels(siteInfo, isChecked, checkedLevel, true);

                return Ok(new
                {
                    Value = columns,
                    CheckedLevels = checkedLevels,
                    CheckedLevel = checkedLevel
                });
            }
            catch (Exception ex)
            {
                LogUtils.AddErrorLog(ex);
                return InternalServerError(ex);
            }
        }

        [HttpPost, Route(Route)]
        public IHttpActionResult Submit()
        {
            try
            {
                var request = new AuthenticatedRequest();

                var downloadUrl = string.Empty;

                var siteId = request.GetPostInt("siteId");
                var channelId = request.GetPostInt("channelId");
                var exportType = request.GetPostString("exportType");
                var isAllCheckedLevel = request.GetPostBool("isAllCheckedLevel");
                var checkedLevelKeys = request.GetPostObject<List<int>>("checkedLevelKeys");
                var isAllDate = request.GetPostBool("isAllDate");
                var startDate = request.GetPostDateTime("startDate", DateTime.Now);
                var endDate = request.GetPostDateTime("endDate", DateTime.Now);
                var columnNames = request.GetPostObject<List<string>>("columnNames");

                if (!request.IsUserLoggin ||
                    !request.UserPermissionsImpl.HasChannelPermissions(siteId, channelId,
                        ConfigManager.ChannelPermissions.ChannelEdit))
                {
                    return Unauthorized();
                }

                var siteInfo = SiteManager.GetSiteInfo(siteId);
                if (siteInfo == null) return BadRequest("无法确定内容对应的站点");

                var channelInfo = ChannelManager.GetChannelInfo(siteId, channelId);
                if (channelInfo == null) return BadRequest("无法确定内容对应的栏目");

                var adminId = request.AdminPermissionsImpl.GetAdminId(siteId, channelId);

                var columns = ContentManager.GetContentColumns(siteInfo, channelInfo, true);
                var pluginIds = PluginContentManager.GetContentPluginIds(channelInfo);
                var pluginColumns = PluginContentManager.GetContentColumns(pluginIds);

                var contentInfoList = new List<ContentInfo>();
                var ccIds = DataProvider.ContentDao.GetCacheChannelContentIdList(siteInfo, channelInfo, adminId, true);
                var count = ccIds.Count;

                var pages = Convert.ToInt32(Math.Ceiling((double)count / siteInfo.Additional.PageSize));
                if (pages == 0) pages = 1;

                if (count > 0)
                {
                    for (var page = 1; page <= pages; page++)
                    {
                        var offset = siteInfo.Additional.PageSize * (page - 1);
                        var limit = siteInfo.Additional.PageSize;
                        var pageCcIds = ccIds.Skip(offset).Take(limit).ToList();

                        var sequence = offset + 1;

                        foreach (var channelContentId in pageCcIds)
                        {
                            var contentInfo = ContentManager.GetContentInfo(siteInfo, channelContentId.ChannelId, channelContentId.ContentId);
                            if (contentInfo == null) continue;

                            if (!isAllCheckedLevel)
                            {
                                var checkedLevel = contentInfo.CheckedLevel;
                                if (contentInfo.IsChecked)
                                {
                                    checkedLevel = siteInfo.Additional.CheckContentLevel;
                                }
                                if (!checkedLevelKeys.Contains(checkedLevel))
                                {
                                    continue;
                                }
                            }

                            if (!isAllDate)
                            {
                                if (contentInfo.AddDate < startDate || contentInfo.AddDate > endDate)
                                {
                                    continue;
                                }
                            }

                            contentInfoList.Add(ContentManager.Calculate(sequence++, contentInfo, columns, pluginColumns));
                        }
                    }

                    if (contentInfoList.Count > 0)
                    {
                        if (exportType == "zip")
                        {
                            var fileName = $"{channelInfo.ChannelName}.zip";
                            var filePath = PathUtils.GetTemporaryFilesPath(fileName);
                            var exportObject = new ExportObject(siteId, request.AdminName);
                            contentInfoList.Reverse();
                            if (exportObject.ExportContents(filePath, contentInfoList))
                            {
                                downloadUrl = PageUtils.GetTemporaryFilesUrl(fileName);
                            }
                        }
                        else if (exportType == "excel")
                        {
                            var fileName = $"{channelInfo.ChannelName}.csv";
                            var filePath = PathUtils.GetTemporaryFilesPath(fileName);
                            ExcelObject.CreateExcelFileForContents(filePath, siteInfo, channelInfo, contentInfoList, columnNames);
                            downloadUrl = PageUtils.GetTemporaryFilesUrl(fileName);
                        }
                    }
                }

                return Ok(new
                {
                    Value = downloadUrl,
                    IsSuccess = !string.IsNullOrEmpty(downloadUrl)
                });
            }
            catch (Exception ex)
            {
                LogUtils.AddErrorLog(ex);
                return InternalServerError(ex);
            }
        }
    }
}
