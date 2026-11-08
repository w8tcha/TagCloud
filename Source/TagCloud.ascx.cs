/*  *********************************************************************************************
*
*   TagCloud - This Module displays the most frequently used words (Tags) of your Portal as a
*   standard Web 2.0 Tag Cloud, or You can define your own Tags list.  The Tags are links which linked to the Portal Search to
*   show all Pages with that Tag.
*
*   The Tag Cloud will be rendered as 3D Cloud, and
*    as a list of hyperlinks in varying styles depending on a weight.
*   This is similar to tag clouds in del.icio.us or Flickr.
*
*   Copyright(c) Ingo Herbote
*   All rights reserved.
*   Internet: https://github.com/w8tcha/TagCloud
*
*   TagCloud is released under the New BSD License, see below
************************************************************************************************
*
*   Redistribution and use in source and binary forms, with or without modification,
*   are permitted provided that the following conditions are met:
*
*   * Redistributions of source code must retain the above copyright notice,
*   this list of conditions and the following disclaimer.
*
*   * Redistributions in binary form must reproduce the above copyright notice,
*   this list of conditions and the following disclaimer in the documentation and/
*   or other materials provided with the distribution.
*
*   * Neither the name of Ingo Herbote nor the names of its contributors
*   may be used to endorse or promote products derived from this software without
*   specific prior written permission.
*
*   THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND
*   ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED WARRANTIES
*   OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED.
*   IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT,
*   INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO,
*   PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS
*   INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT
*   LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE
*   OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
*
************************************************************************************************
*/

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading;
using System.Web;
using System.Web.UI;
using System.Web.UI.HtmlControls;

using DNN.Modules.Constants;
using DNN.Modules.Objects;
using DNN.Modules.VRK.Controls;

using DotNetNuke.Abstractions;
using DotNetNuke.Abstractions.ClientResources;
using DotNetNuke.Common.Utilities;
using DotNetNuke.Entities.Content.Common;
using DotNetNuke.Entities.Content.Taxonomy;
using DotNetNuke.Entities.Modules;
using DotNetNuke.Entities.Modules.Actions;
using DotNetNuke.Entities.Portals;
using DotNetNuke.Entities.Tabs;
using DotNetNuke.Security;
using DotNetNuke.Security.Permissions;
using DotNetNuke.Services.Exceptions;
using DotNetNuke.Services.Localization;

using Microsoft.Extensions.DependencyInjection;

namespace DNN.Modules
{
    using TagController = Ventrian.SimpleGallery.Entities.TagController;
    using TagInfo = Ventrian.SimpleGallery.Entities.TagInfo;

    /// <summary>
    /// The tag cloud.
    /// </summary>
    public partial class TagCloud : ModuleSettingsBase, IActionable
    {
        /// <summary>
        /// The navigation manager.
        /// </summary>
        private readonly INavigationManager navigationManager;

        /// <summary>
        /// Opens a script block that runs once the DOM is ready (vanilla replacement for document ready).
        /// </summary>
        private const string DomReadyStart =
            "(function () { var start = function () {";

        /// <summary>
        /// Closes the script block opened by <see cref="DomReadyStart"/>.
        /// </summary>
        private const string DomReadyEnd =
            "}; if (document.readyState === 'loading') { document.addEventListener('DOMContentLoaded', start); } else { start(); } })();";

        /// <summary>
        /// The client resource controller.
        /// </summary>
        private readonly IClientResourceController clientResourceController;

        /// <summary>
        /// The vocabularies.
        /// </summary>
        private string[] vocabularies;

        /// <summary>
        /// The Module Settings
        /// </summary>
        private TagCloudSettings settings;

        /// <summary>
        /// Initializes a new instance of the <see cref="TagCloud"/> class.
        /// </summary>
        protected TagCloud()
        {
            this.navigationManager = this.DependencyProvider.GetRequiredService<INavigationManager>();
            this.clientResourceController = this.DependencyProvider.GetRequiredService<IClientResourceController>();
        }

        /// <summary>
        ///  Gets Add Menu Entries to Module Container
        /// </summary>
        public ModuleActionCollection ModuleActions
        {
            get
            {
                var actions = new ModuleActionCollection
                    {
                        {
                            this.GetNextActionID(), Localization.GetString("Manage.Text", this.LocalResourceFile),
                            ModuleActionType.AddContent, string.Empty, string.Empty, this.EditUrl(), false,
                            SecurityAccessLevel.Edit, true, false
                            }
                    };

                return actions;
            }
        }

        /// <summary>
        /// Raises the <see cref="E:System.Web.UI.Control.Init" /> event.
        /// </summary>
        /// <param name="e">An <see cref="T:System.EventArgs" /> object that contains the event data.</param>
        protected override void OnInit(EventArgs e)
        {
            this.InitializeComponent();
            base.OnInit(e);
        }

        /// <summary>
        /// Main Entry to load Tags
        /// </summary>
        /// <returns>
        /// Item List
        /// </returns>
        private List<CloudItem> GetItems()
        {
            // Localized tags, URLs and common words differ per language, so the cache is per language
            var cacheKey = TagCloudController.GetItemsCacheKey(
                this.TabModuleId,
                Thread.CurrentThread.CurrentUICulture.Name);

            var itemList = this.settings.CacheItems ? DataCache.GetCache(cacheKey) as List<CloudItem> : null;

            if (itemList == null || itemList.Count == 0)
            {
                itemList = this.GetItemsFromDb(cacheKey);
            }

            switch (this.settings.RenderMode)
            {
                case RenderMode.Html5:
                    {
                        this.AddCanvasScript();
                    }

                    break;
                case RenderMode.WordCloud:
                    {
                        this.AddWordCloudScript(itemList);
                    }

                    break;
            }

            return itemList;
        }

        /// <summary>
        /// Get Tag Items From the Database
        /// </summary>
        /// <param name="cacheKey">The cache key for the tags.</param>
        /// <returns>
        /// List of Tags
        /// </returns>
        private List<CloudItem> GetItemsFromDb(string cacheKey)
        {
            var list = new List<CloudItem>();

            if (this.settings.ModeCustom)
            {
                if (this.settings.TagsLinkChk)
                {
                    this.settings.CacheItems = false;
                }

                list.AddRange(this.GetItemsFromCustom());
            }

            if (this.settings.ModeTax)
            {
                this.settings.TagsLinkChk = false;
                list.AddRange(this.GetItemsFromTax());
            }

            if (this.settings.ModeNewsarticles && Utility.IsNewsArticlesInstalled())
            {
                this.settings.TagsLinkChk = false;
                list.AddRange(this.GetItemsFromNewsArticles());
            }

            if (this.settings.ModeSimplegallery && Utility.IsSimplyGalleryInstalled())
            {
                this.settings.TagsLinkChk = false;
                list.AddRange(this.GetItemsFromSimpleGallery());
            }

            if (this.settings.ModeActiveForums && Utility.IsActiveForumsInstalled())
            {
                this.settings.TagsLinkChk = false;

                // The tags depend on the forums the current user can see, they must not be cached for all users
                this.settings.CacheItems = false;

                list.AddRange(this.GetItemsFromActiveForums());
            }

            // Exclude Words
            if (this.settings.ExclusionWords.Count > 0)
            {
                foreach (var exWord in this.settings.ExclusionWords)
                {
                    var value = exWord.Word.ToLower();

                    switch (exWord.ExcludeWordType)
                    {
                        case ExcludeType.Contains:
                            {
                                list.RemoveAll(check => check.Text.ToLower().Contains(value));
                            }

                            break;
                        case ExcludeType.Equals:
                            {
                                list.RemoveAll(check => check.Text.ToLower().Equals(value));
                            }

                            break;
                        case ExcludeType.StartsWith:
                            {
                                list.RemoveAll(check => check.Text.ToLower().StartsWith(value));
                            }

                            break;
                        case ExcludeType.EndsWith:
                            {
                                list.RemoveAll(check => check.Text.ToLower().EndsWith(value));
                            }

                            break;
                    }
                }
            }

            // Keep only the tags with the highest weight (in their original order), before sorting
            if (this.settings.TagCount > 0 && list.Count > this.settings.TagCount)
            {
                var topItems = new HashSet<CloudItem>(
                    list.OrderByDescending(item => item.Weight).Take(this.settings.TagCount));

                list = list.Where(topItems.Contains).ToList();
            }

            // Sort the List?
            switch (this.settings.CloudSortType)
            {
                case SortType.AlphabeticAsc:
                    {
                        Utility.SortAscending(list, item => item.Text);
                    }

                    break;
                case SortType.AlphabeticDsc:
                    {
                        Utility.SortDescending(list, item => item.Text);
                    }

                    break;
                case SortType.WeightedAsc:
                    {
                        Utility.SortAscending(list, item => item.Weight);
                    }

                    break;
                case SortType.WeightedDsc:
                    {
                        Utility.SortDescending(list, item => item.Weight);
                    }

                    break;
            }

            if (this.settings.CacheItems)
            {
                // Absolute expiration, so tags from other modules and the taxonomy get refreshed
                DataCache.SetCache(cacheKey, list, DateTime.Now.Add(TagCloudController.ItemsCacheDuration));
            }

            return list;
        }

        /// <summary>
        /// Check if the User can see the Page
        /// </summary>
        /// <param name="portalId">
        /// Portal ID
        /// </param>
        /// <param name="tabId">
        /// Tab to Check
        /// </param>
        /// <returns>
        /// Returns if the User can see the Page or not.
        /// </returns>
        private static bool UserSeeTag(int portalId, int tabId)
        {
            var canSee = false;

            var tabPermissions = TabPermissionController.GetTabPermissions(tabId, portalId);

            // Get The Tab
            if (PortalSecurity.IsInRoles(tabPermissions.ToString("VIEW")))
            {
                canSee = true;
            }

            return canSee;
        }

        /// <summary>
        /// Build Entire Script Link and Block for the HTML5 Canvas Tag Cloud
        /// </summary>
        private void AddCanvasScript()
        {
            if (HttpContext.Current.Items["tagcanvas_registered"] == null)
            {
                ScriptManager.RegisterClientScriptInclude(
                    this, typeof(Page), "tagcanvas", this.ResolveUrl("js/tagcanvas.min.js"));

                HttpContext.Current.Items.Add("tagcanvas_registered", "true");
            }

            var canvasControl = new HtmlGenericControl("canvas");

            canvasControl.Attributes.Add("width", this.settings.FlashWidth);
            canvasControl.Attributes.Add("height", this.settings.FlashHeight);

            this.tagCloudDiv.Controls.Add(canvasControl);

            this.tagCloudDiv.Controls.Remove(this.c1);

            canvasControl.Controls.Add(this.c1);

            var canvasScript = new StringBuilder();

            canvasScript.Append(DomReadyStart);

            if (!this.settings.Bgcolor.Contains("transparent"))
            {
                var backColor = this.settings.Bgcolor.Replace("0x", "#");

                canvasScript.AppendFormat(
                    "document.getElementById('{0}').style.backgroundColor = '{1}';",
                    canvasControl.ClientID,
                    backColor);
            }

            canvasScript.AppendFormat("try {{ TagCanvas.Start('{0}', undefined, {{", canvasControl.ClientID);

            var textColor = this.settings.Tcolor.Replace("0x", "#");
            var outlineColor = this.settings.Hicolor.Replace("0x", "#");

            canvasScript.AppendFormat("textFont : '{0}',", HttpUtility.JavaScriptStringEncode(this.settings.FontFamily));
            canvasScript.AppendFormat("textColour : '{0}',", textColor);
            canvasScript.AppendFormat("outlineColour : '{0}',", outlineColor);
            canvasScript.Append("maxSpeed : 0.03,");
            canvasScript.Append("outlineThickness : 1,");
            canvasScript.Append("weight : true,");
            canvasScript.Append("weightFrom : 'data-weight',");
            canvasScript.AppendFormat("weightMode : '{0}',", this.settings.WeightMode);
            // Emitted as number, the setting is never written as text into the script
            var weightSize = ParseDouble(this.settings.WeightSize, 1.0);

            canvasScript.AppendFormat(
                CultureInfo.InvariantCulture,
                "weightSize : {0}",
                weightSize > 0 ? weightSize : 1.0);

            // TagCanvas throws if the browser has no canvas support, the tag list is then shown as fallback
            canvasScript.Append("}); } catch (e) { }");

            canvasScript.Append(DomReadyEnd);

            ScriptManager.RegisterStartupScript(
                this,
                typeof(Page),
                $"CanvasScript{this.tagCloudDiv.ClientID}",
                canvasScript.ToString(),
                true);
        }

        /// <summary>
        /// Build Entire Script Link and Block for the HTML5 Canvas Tag Cloud
        /// </summary>
        /// <param name="cloudItems">The item list.</param>
        private void AddWordCloudScript(IEnumerable<CloudItem> cloudItems)
        {
            if (HttpContext.Current.Items["wordcloudjs_registered"] == null)
            {
                ScriptManager.RegisterClientScriptInclude(
                    this,
                    typeof(Page),
                    "wordcloudjs",
                    this.ResolveUrl("js/wordcloud.js"));

                HttpContext.Current.Items.Add("wordcloudjs_registered", "true");
            }

            var canvasControl = new HtmlGenericControl("canvas");

            canvasControl.Attributes.Add("width", this.settings.FlashWidth);
            canvasControl.Attributes.Add("height", this.settings.FlashHeight);

            this.tagCloudDiv.Controls.Add(canvasControl);

            this.tagCloudDiv.Controls.Remove(this.c1);

            canvasControl.Controls.Add(this.c1);

            var canvasScript = new StringBuilder();

            canvasScript.Append(DomReadyStart);

            // Setup WordCloud
            canvasScript.AppendFormat("WordCloud('{0}', {{", canvasControl.ClientID);

            canvasScript.AppendFormat("fontFamily:'{0}',", HttpUtility.JavaScriptStringEncode(this.settings.FontFamily));
            canvasScript.AppendFormat(CultureInfo.InvariantCulture, "gridSize: {0},", this.settings.WordCloudSettings.GridSize);
            canvasScript.AppendFormat(CultureInfo.InvariantCulture, "ellipticity: {0},", this.settings.WordCloudSettings.Ellipticity);
            canvasScript.Append("center: false,");

            if (!this.settings.Bgcolor.Contains("transparent"))
            {
                var backColor = this.settings.Bgcolor.Replace("0x", "#");

                canvasScript.AppendFormat("backgroundColor: '{0}',", backColor);
            }
            else
            {
                canvasScript.Append("backgroundColor: 'transparent',");
            }

            canvasScript.AppendFormat(CultureInfo.InvariantCulture, "weightFactor: {0},", this.settings.WordCloudSettings.WeightFactor);
            canvasScript.AppendFormat(CultureInfo.InvariantCulture, "minSize: {0},", this.settings.WordCloudSettings.MinSize);
            canvasScript.Append("clearCanvas: true,");
            canvasScript.AppendFormat("fillBox: {0},", this.settings.WordCloudSettings.FillBox.ToString().ToLower());
            canvasScript.AppendFormat(
                "shape: '{0}',",
                this.settings.WordCloudSettings.Shape.ToString().Replace("_", "-"));

            canvasScript.Append("wordList:  [");

            var currentItem = 1;
            var enumerable = cloudItems as IList<CloudItem> ?? cloudItems.ToList();
            var itemCount = enumerable.Count;

            // The word cloud uses weight * weightFactor as font size, raw weights (usage counts) can be far
            // too large for the canvas, so the weights are scaled to a fixed range
            var minWeight = itemCount > 0 ? enumerable.Min(item => item.Weight) : 0;
            var maxWeight = itemCount > 0 ? enumerable.Max(item => item.Weight) : 0;

            foreach (var item in enumerable)
            {
                canvasScript.AppendFormat(
                    CultureInfo.InvariantCulture,
                    currentItem.Equals(itemCount) ? "['{0}', {1}]" : "['{0}', {1}],",
                    HttpUtility.JavaScriptStringEncode(item.Text),
                    Math.Round(ScaleWordCloudWeight(item.Weight, minWeight, maxWeight), 1));

                currentItem++;
            }

            canvasScript.Append("]");

            canvasScript.Append("});");

            canvasScript.Append(DomReadyEnd);

            ScriptManager.RegisterStartupScript(
                this,
                typeof(Page),
                $"CanvasScript{this.tagCloudDiv.ClientID}",
                canvasScript.ToString(),
                true);
        }

        /// <summary>
        /// Scales a tag weight linear to the range used by the word cloud (5 - 25, multiplied with the weight factor).
        /// </summary>
        /// <param name="weight">The tag weight.</param>
        /// <param name="minWeight">The lowest weight of all tags.</param>
        /// <param name="maxWeight">The highest weight of all tags.</param>
        /// <returns>The scaled weight.</returns>
        private static double ScaleWordCloudWeight(double weight, double minWeight, double maxWeight)
        {
            const double MinScaled = 5;
            const double MaxScaled = 25;

            if (maxWeight - minWeight < double.Epsilon)
            {
                return (MinScaled + MaxScaled) / 2;
            }

            return MinScaled + ((MaxScaled - MinScaled) * (weight - minWeight) / (maxWeight - minWeight));
        }

        /// <summary>
        /// Appends a query string to a URL, with '&amp;' if the URL already has a query string (no friendly URLs).
        /// </summary>
        /// <param name="url">The URL.</param>
        /// <param name="queryString">The query string, e.g. "Tag=value".</param>
        /// <returns>The URL with query string.</returns>
        private static string AppendQueryString(string url, string queryString)
        {
            return $"{url}{(url.Contains("?") ? "&" : "?")}{queryString}";
        }

        /// <summary>
        /// Parses a whole number setting culture independent. Decimal values (e.g. "8.5") are rounded.
        /// </summary>
        /// <param name="value">The setting value.</param>
        /// <param name="defaultValue">The value returned if the setting is empty or invalid.</param>
        /// <returns>The parsed value.</returns>
        private static int ParseInt(string value, int defaultValue)
        {
            var number = ParseDouble(value, double.NaN);

            return double.IsNaN(number) || number < int.MinValue || number > int.MaxValue
                       ? defaultValue
                       : (int)Math.Round(number);
        }

        /// <summary>
        /// Parses a decimal setting culture independent. Accepts '.' and ',' as decimal separator,
        /// because older versions could save the value with the decimal separator of the current culture.
        /// </summary>
        /// <param name="value">The setting value.</param>
        /// <param name="defaultValue">The value returned if the setting is empty or invalid.</param>
        /// <returns>The parsed value.</returns>
        private static double ParseDouble(string value, double defaultValue)
        {
            return !string.IsNullOrWhiteSpace(value)
                   && double.TryParse(
                       value.Trim().Replace(',', '.'),
                       NumberStyles.Float,
                       CultureInfo.InvariantCulture,
                       out var result)
                   && !double.IsNaN(result)
                   && !double.IsInfinity(result)
                       ? result
                       : defaultValue;
        }

        /// <summary>
        /// Format the Url from FileID to File Path Url
        /// </summary>
        /// <param name="sInputUrl">
        /// The Input Url.
        /// </param>
        /// <returns>
        /// Returns the formatted URL
        /// </returns>
        private string FormatUrl(string sInputUrl)
        {
            // Only a page (tab) id needs to be resolved, any other URL (http, https, relative, ...) is used as is
            if (string.IsNullOrEmpty(sInputUrl)
                || !int.TryParse(sInputUrl, NumberStyles.None, CultureInfo.InvariantCulture, out var tabId))
            {
                return sInputUrl;
            }

            var objTabInfo = TabController.Instance.GetTab(tabId, this.PortalSettings.PortalId, true);

            // The page was deleted, render the tag without link
            return objTabInfo == null || objTabInfo.IsDeleted ? null : objTabInfo.FullUrl;
        }

        /// <summary>
        /// Get Tags from ActiveForums Module
        /// </summary>
        /// <returns>
        /// List of Tags
        /// </returns>
        private IEnumerable<CloudItem> GetItemsFromActiveForums()
        {
            var list = new List<CloudItem>();

            try
            {
                ////////////////////////
                foreach (var tag in
                    DataControl.TagCloudActiveForumsTags(this.PortalSettings.PortalId, this.settings.AfModule, this.settings.TagCount).Where(
                        tag => tag.Weight >= this.settings.OccurCount))
                {
                    CloudItem entry;

                    if (this.settings.TagsLink)
                    {
                        entry = new CloudItem(
                            Utility.RemoveIllegalCharacters(tag.Text),
                            tag.Weight,
                            this.navigationManager.NavigateURL(this.settings.AfTab, string.Empty, "afv=search", $"aftg={Uri.EscapeDataString(tag.Text)}"),
                            tag.Text);
                    }
                    else
                    {
                        entry = new CloudItem(Utility.RemoveIllegalCharacters(tag.Text), tag.Weight, null, tag.Text);
                    }

                    list.Add(entry);
                }
            }
            catch (Exception exc)
            {
                Exceptions.ProcessModuleLoadException(this, exc);
            }

            return list;
        }

        /// <summary>
        /// Get Custom Tag Items From the Sql Tables
        /// </summary>
        /// <returns>
        /// List of Tags
        /// </returns>
        private IEnumerable<CloudItem> GetItemsFromCustom()
        {
            var list = new List<CloudItem>();

            try
            {
                ////////////////////////
                foreach (var tag in DataControl.TagCloudItemsGetByModule(this.ModuleId).Where(tag => tag.Weight >= this.settings.OccurCount).Where(tag => !Utility.IsNumeric(tag.Url) || !this.settings.TagsLinkChk || UserSeeTag(this.PortalSettings.PortalId, int.Parse(tag.Url))))
                {
                    CloudItem entry;

                    // Has Localized Value?
                    var currentCulture = Thread.CurrentThread.CurrentUICulture;

                    tag.LocalTags = DataControl.TagCloudItemsGetByLocale(this.ModuleId, tag.TagId);

                    string sTag = null, sTagUrl = null;

                    var localizedTag = tag.LocalTags.FirstOrDefault(locales => locales.Locale.Equals(currentCulture.ToString()));

                    if (localizedTag != null)
                    {
                        sTag = localizedTag.TagMl;
                        sTagUrl = localizedTag.UrlMl;
                    }

                    if (string.IsNullOrEmpty(sTag))
                    {
                        sTag = tag.Tag;
                    }

                    if (string.IsNullOrEmpty(sTagUrl))
                    {
                        sTagUrl = tag.Url;
                    }

                    if (this.settings.TagsLink)
                    {
                        entry = new CloudItem(
                            Utility.RemoveIllegalCharacters(sTag), tag.Weight, this.FormatUrl(sTagUrl), sTag);
                    }
                    else
                    {
                        entry = new CloudItem(Utility.RemoveIllegalCharacters(sTag), tag.Weight, null, sTag);
                    }

                    list.Add(entry);
                }
            }
            catch (Exception exc)
            {
                Exceptions.ProcessModuleLoadException(this, exc);
            }

            return list;
        }

        /// <summary>
        /// Get Tags from Ventrian.NewsArticles Module
        /// </summary>
        /// <returns>
        /// List of Tags
        /// </returns>
        private IEnumerable<CloudItem> GetItemsFromNewsArticles()
        {
            var list = new List<CloudItem>();

            var tc = new Ventrian.NewsArticles.TagController();

            try
            {
                ////////////////////////
                foreach (var tag in
                    tc.List(this.settings.VentrianModuleNews, this.settings.TagCount).Cast<Ventrian.NewsArticles.TagInfo>().Where(
                        tag => tag.Usages >= this.settings.OccurCount))
                {
                    CloudItem entry;

                    if (this.settings.TagsLink)
                    {
                        entry = new CloudItem(
                            Utility.RemoveIllegalCharacters(tag.Name),
                            tag.Usages,
                            this.navigationManager.NavigateURL(
                                this.settings.VentrianTabNews,
                                string.Empty,
                                $"articletype=tagview&tag={this.Server.UrlEncode(tag.NameLowered)}"),
                            tag.Name);
                    }
                    else
                    {
                        entry = new CloudItem(Utility.RemoveIllegalCharacters(tag.Name), tag.Usages, null, tag.Name);
                    }

                    list.Add(entry);
                }
            }
            catch (Exception exc)
            {
                Exceptions.ProcessModuleLoadException(this, exc);
            }

            return list;
        }

        /// <summary>
        /// Get Tags from Ventrian.SimpleGallery Module
        /// </summary>
        /// <returns>
        /// List of Tags
        /// </returns>
        private IEnumerable<CloudItem> GetItemsFromSimpleGallery()
        {
            var list = new List<CloudItem>();

            var tc = new TagController();

            var simpleGalleryInstance = ModuleController.Instance.GetModule(this.settings.VentrianModuleSimple, this.settings.VentrianTabSimple, false);

            try
            {
                ////////////////////////
                foreach (var tag in
                    tc.List(this.settings.VentrianModuleSimple, -1, this.settings.TagCount, true).Cast<TagInfo>().Where(
                        tag => tag.Usages >= this.settings.OccurCount))
                {
                    CloudItem entry;

                    if (this.settings.TagsLink)
                    {
                        entry = new CloudItem(
                            Utility.RemoveIllegalCharacters(tag.Name),
                            tag.Usages,
                            this.navigationManager.NavigateURL(
                                this.settings.VentrianTabSimple,
                                string.Empty,
                                $"Tag={Uri.EscapeDataString(tag.Name)}",
                                $"Tags={simpleGalleryInstance.TabModuleID}"),
                            tag.Name);
                    }
                    else
                    {
                        entry = new CloudItem(Utility.RemoveIllegalCharacters(tag.Name), tag.Usages, null, tag.Name);
                    }

                    list.Add(entry);
                }
            }
            catch (Exception exc)
            {
                Exceptions.ProcessModuleLoadException(this, exc);
            }

            return list;
        }

        /// <summary>
        /// Get Custom Tag Items From the DNN Taxonomy
        /// </summary>
        /// <returns>
        /// List of Tags
        /// </returns>
        private IEnumerable<CloudItem> GetItemsFromTax()
        {
            var list = new List<CloudItem>();

            try
            {
                // The weight of a term is how often it is used, terms that are not used get the lowest weight
                var usageCounts = DataControl.GetTermUsageCounts();

                foreach (var term in this.GetTerms())
                {
                    var weight = usageCounts.TryGetValue(term.TermId, out var count) && count > 0 ? count : 1;

                    var entry = new CloudItem(
                        Utility.RemoveIllegalCharacters(term.Name),
                        weight,
                        AppendQueryString(
                            this.navigationManager.NavigateURL(this.settings.SearchTabId),
                            this.settings.SearchTaxQueryString + Uri.EscapeDataString(Utility.RemoveIllegalCharacters(term.Name))),
                        term.Description);

                    list.Add(entry);
                }
            }
            catch (Exception exc)
            {
                Exceptions.ProcessModuleLoadException(this, exc);
            }

            return list;
        }

        /// <summary>
        /// Get Dnn Taxonomy Terms
        /// </summary>
        /// <returns>
        /// The Item List
        /// </returns>
        private IEnumerable<Term> GetTerms()
        {
            var terms = new List<Term>();

            switch (this.settings.TaxMode)
            {
                case "tab":
                    {
                        terms = this.PortalSettings.ActiveTab.Terms;
                    }

                    break;
                case "all":
                    {
                        var termRep = this.DependencyProvider.GetRequiredService<ITermController>();

                        var vocabRep = this.DependencyProvider.GetRequiredService<IVocabularyController>();

                        var vs = from v in vocabRep.GetVocabularies()
                                                    where
                                                        v.ScopeType.ScopeType == "Application" ||
                                                        v.ScopeType.ScopeType == "Portal" && v.ScopeId == this.PortalSettings.PortalId
                                                    select v;

                        foreach (var v in vs)
                        {
                            foreach (var t in termRep.GetTermsByVocabulary(v.VocabularyId))
                            {
                                if (v.Type == VocabularyType.Simple)
                                {
                                    t.ParentTermId = -v.VocabularyId;
                                }

                                terms.Add(t);
                            }
                        }
                    }

                    break;
                case "custom":
                    {
                        if (this.vocabularies != null)
                        {
                            var termRep = this.DependencyProvider.GetRequiredService<ITermController>();

                            foreach (var sVocabularyId in this.vocabularies)
                            {
                                terms.AddRange(termRep.GetTermsByVocabulary(int.Parse(sVocabularyId)));
                            }
                        }
                    }

                    break;
            }

            return terms;
        }

        /// <summary>
        /// Required method for Designer support - do not modify
        ///  the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            try
            {
                this.LoadModuleSettings();
            }
            catch (Exception exception)
            {
                Exceptions.ProcessModuleLoadException(this, exception);
            }

            // Add Tag Separator
            if (!string.IsNullOrEmpty(this.settings.TagSeparator))
            {
                // The separator is rendered as HTML: entities (e.g. &middot; or &nbsp;|) keep working,
                // markup is shown as text, so the setting can't inject scripts into the page
                this.c1.ItemSeparator = WebUtility.HtmlEncode(WebUtility.HtmlDecode(this.settings.TagSeparator));
            }

            if (this.settings.RenderUl)
            {
                this.c1.RenderAsUl = true;
            }

            this.c1.DataSource = this.GetItems();
            this.c1.DataBind();
        }

        /// <summary>
        /// Load the Setting for this Module
        /// </summary>
        private void LoadModuleSettings()
        {
            var moduleSettings = this.TabModuleSettings;

            // Set Default Settings
            this.settings = new TagCloudSettings
            {
                CacheItems = true,
                StartDate = DateTime.Parse("01/01/1999", new CultureInfo("en-US")),
                RenderUl = true,
                TagsLink = true,
                ExclusionWords = new List<ExcludeWord>(),
                ExcludeCommon = true,
                AfModule = -1,
                AfTab = -1,
                DnnBlogTab = -1,
                VentrianModuleNews = -1,
                VentrianModuleSimple = -1,
                VentrianTabNews = -1,
                VentrianTabSimple = -1,
                WeightMode = WeightMode.size,
                WeightSize = "1.0",
                FontFamily = "Georgia, Arial, sans-serif",
                WordCloudSettings = new WordCloudSettings
                {
                    Ellipticity = 0.85,
                    GridSize = 8,
                    MinSize = 0,
                    Shape = WordCloudShape.circle,
                    WeightFactor = 2.1,
                    FillBox = false
                }
            };

            ////////////////////////

            // Setting Font Family
            if (!string.IsNullOrEmpty((string)moduleSettings["FontFamily"]))
            {
                try
                {
                    this.settings.FontFamily = (string)moduleSettings["FontFamily"];
                }
                catch (Exception)
                {
                    this.settings.FontFamily = "Georgia, Arial, sans-serif";
                }
            }

            // Setting WeightMode Type
            if (!string.IsNullOrEmpty((string)moduleSettings["WeightMode"]))
            {
                try
                {
                    this.settings.WeightMode = (WeightMode)Enum.Parse(
                        typeof(WeightMode),
                        (string)moduleSettings["WeightMode"]);
                }
                catch (Exception)
                {
                    this.settings.WeightMode = WeightMode.size;
                }
            }

            // Setting WeightSize
            if (!string.IsNullOrEmpty((string)moduleSettings["WeightSize"]))
            {
                try
                {
                    this.settings.WeightSize = (string)moduleSettings["WeightSize"];
                }
                catch (Exception)
                {
                    this.settings.WeightSize = "1.0";
                }
            }

            // Setting RenderMode Type
            if (!string.IsNullOrEmpty((string)moduleSettings["RenderMode"]))
            {
                try
                {
                    this.settings.RenderMode = (RenderMode)Enum.Parse(
                        typeof(RenderMode),
                        (string)moduleSettings["RenderMode"]);
                }
                catch (Exception)
                {
                    this.settings.RenderMode = RenderMode.Html5;
                }
            }
            else
            {
                this.settings.RenderMode = RenderMode.Html5;
            }

            // Setting Render Item Weight
            if (!string.IsNullOrEmpty((string)moduleSettings["RenderItemWeight"]))
            {
                bool.TryParse((string)moduleSettings["RenderItemWeight"], out var renderItemWeight);

                this.settings.RenderItemWeight = renderItemWeight;
            }

            // Get Search Tab
            if (!string.IsNullOrEmpty((string)this.TabModuleSettings["CustomSearchPage"]))
            {
                this.settings.SearchTabId = Convert.ToInt32((string)this.TabModuleSettings["CustomSearchPage"]);
            }
            else
            {
                var portal = PortalController.Instance.GetPortal(this.PortalSettings.PortalId);

                this.settings.SearchTabId = portal.SearchTabId;
            }

            this.settings.SearchQueryString =
                !string.IsNullOrEmpty((string)this.TabModuleSettings["SearchPageQueryString"])
                    ? (string)this.TabModuleSettings["SearchPageQueryString"]
                    : "Search=";

            this.settings.SearchTaxQueryString =
                !string.IsNullOrEmpty((string)this.TabModuleSettings["SearchPageTaxQueryString"])
                    ? (string)this.TabModuleSettings["SearchPageTaxQueryString"]
                    : "Tag=";

            if (!string.IsNullOrEmpty((string)moduleSettings["TagSeparator"]))
            {
                try
                {
                    this.settings.TagSeparator = (string)moduleSettings["TagSeparator"];
                }
                catch (Exception)
                {
                    this.settings.TagSeparator = string.Empty;
                }
            }
            else
            {
                this.settings.TagSeparator = string.Empty;
            }

            if (!string.IsNullOrEmpty((string)moduleSettings["ModeCustom"]))
            {
                bool.TryParse((string)moduleSettings["ModeCustom"], out var modeCustom);

                this.settings.ModeCustom = modeCustom;
            }

            if (!string.IsNullOrEmpty((string)moduleSettings["ModeTax"]))
            {
                bool.TryParse((string)moduleSettings["ModeTax"], out var modeTax);

                this.settings.ModeTax = modeTax;
            }

            if (!string.IsNullOrEmpty((string)moduleSettings["ModeNewsarticles"]))
            {
                bool.TryParse((string)moduleSettings["ModeNewsarticles"], out var modenews);

                this.settings.ModeNewsarticles = modenews;
            }

            if (!string.IsNullOrEmpty((string)moduleSettings["ModeSimplegallery"]))
            {
                bool.TryParse((string)moduleSettings["ModeSimplegallery"], out var modesimple);

                this.settings.ModeSimplegallery = modesimple;
            }

            if (!string.IsNullOrEmpty((string)moduleSettings["ModeActiveforums"]))
            {
                bool.TryParse((string)moduleSettings["ModeActiveforums"], out var modeactive);

                this.settings.ModeActiveForums = modeactive;
            }

            // Load old Setting
            if (!string.IsNullOrEmpty((string)moduleSettings["TagMode"]))
            {
                var sTagMode = (string)moduleSettings["TagMode"];

                switch (sTagMode)
                {
                    case "custom":
                        this.settings.ModeCustom = true;
                        break;
                    case "newsarticles":
                        this.settings.ModeNewsarticles = true;
                        break;
                    case "simplegallery":
                        this.settings.ModeSimplegallery = true;
                        break;
                    case "activeforums":
                        this.settings.ModeActiveForums = true;
                        break;
                    case "tax":
                        this.settings.ModeTax = true;
                        break;
                }
            }

            // Load Ventrian Tab & Module from Settings and Set
            if (!string.IsNullOrEmpty((string)moduleSettings["NewsArticlesTab"]) &&
                !string.IsNullOrEmpty((string)moduleSettings["NewsArticlesModule"]))
            {
                try
                {
                    this.settings.VentrianTabNews = int.Parse((string)moduleSettings["NewsArticlesTab"]);

                    this.settings.VentrianModuleNews = int.Parse((string)moduleSettings["NewsArticlesModule"]);

                    if (this.settings.VentrianTabNews.Equals(-1) || this.settings.VentrianModuleNews.Equals(-1))
                    {
                        this.settings.ModeNewsarticles = false;
                    }
                }
                catch (Exception)
                {
                    this.settings.ModeNewsarticles = false;
                }
            }

            if (!string.IsNullOrEmpty((string)moduleSettings["SimpleGalleryTab"]) &&
                !string.IsNullOrEmpty((string)moduleSettings["SimpleGalleryModule"]))
            {
                try
                {
                    this.settings.VentrianTabSimple = int.Parse((string)moduleSettings["SimpleGalleryTab"]);

                    this.settings.VentrianModuleSimple = int.Parse((string)moduleSettings["SimpleGalleryModule"]);

                    if (this.settings.VentrianTabSimple.Equals(-1) || this.settings.VentrianModuleSimple.Equals(-1))
                    {
                        this.settings.ModeSimplegallery = false;
                    }
                }
                catch (Exception)
                {
                    this.settings.ModeSimplegallery = false;
                }
            }

            if (!string.IsNullOrEmpty((string)moduleSettings["ActiveForumsTab"]) &&
                !string.IsNullOrEmpty((string)moduleSettings["ActiveForumsModule"]))
            {
                try
                {
                    this.settings.AfTab = int.Parse((string)moduleSettings["ActiveForumsTab"]);

                    this.settings.AfModule = int.Parse((string)moduleSettings["ActiveForumsModule"]);

                    if (this.settings.AfTab.Equals(-1) || this.settings.AfModule.Equals(-1))
                    {
                        this.settings.ModeActiveForums = false;
                    }
                }
                catch (Exception)
                {
                    this.settings.ModeActiveForums = false;
                }
            }

            if (!string.IsNullOrEmpty((string)moduleSettings["TaxMode"]))
            {
                try
                {
                    this.settings.TaxMode = (string)moduleSettings["TaxMode"];
                }
                catch (Exception)
                {
                    this.settings.ModeTax = false;
                }
            }
            else
            {
                this.settings.ModeTax = false;
            }

            // Setting TaxMode Vocabularies
            if (!string.IsNullOrEmpty((string)moduleSettings["TaxVocabularies"]))
            {
                var sVocabularies = (string)moduleSettings["TaxVocabularies"];

                this.vocabularies = sVocabularies.Split(';');
            }

            if (!string.IsNullOrEmpty((string)moduleSettings["SkinName"]))
            {
                try
                {
                    this.settings.SkinName = (string)moduleSettings["SkinName"];
                }
                catch (Exception)
                {
                    this.settings.SkinName = "Standard";
                }
            }
            else
            {
                this.settings.SkinName = "Standard";
            }

            if (!this.settings.SkinName.Equals("None"))
            {
                this.clientResourceController
                    .CreateStylesheet($"{this.ResolveUrl("Skins")}/{this.settings.SkinName}.css")
                    .Register();

                this.tagCloudDiv.CssClass += $"-{this.settings.SkinName}";
                this.c1.ItemCssClassPrefix += $"-{this.settings.SkinName}";
            }

            this.settings.OccurCount = ParseInt((string)moduleSettings["occurcount"], 1);

            this.settings.TagCount = ParseInt((string)moduleSettings["tagscount"], 30);

            if (!string.IsNullOrEmpty((string)moduleSettings["ExcludeCommon"]))
            {
                bool.TryParse((string)moduleSettings["ExcludeCommon"], out var excludeCommon);

                this.settings.ExcludeCommon = excludeCommon;
            }

            if (this.settings.ExcludeCommon)
            {
                // Load Common Words, current Language
                var commonWords = Localization.GetString("CommonWords.Text", this.LocalResourceFile);

                if (!string.IsNullOrEmpty(commonWords))
                {
                    var comnmonWordList = commonWords.Split(',');

                    foreach (var word in comnmonWordList)
                    {
                        this.settings.ExclusionWords.Add(
                            new ExcludeWord { Word = word, ExcludeWordType = ExcludeType.Equals });
                    }
                }
            }

            // Import Old List
            var strExlusionList = (string)moduleSettings["exlusLst"];

            if (!string.IsNullOrEmpty(strExlusionList))
            {
                var oldList = strExlusionList.Split(',');

                foreach (var word in oldList)
                {
                    this.settings.ExclusionWords.Add(
                        new ExcludeWord { Word = word, ExcludeWordType = ExcludeType.Equals });
                }
            }

            // Load New Exclude Word List
            this.settings.ExclusionWords.AddRange(DataControl.TagCloudExcludeWordsGetByModule(this.TabModuleId));

            try
            {
                this.settings.CloudSortType = (SortType)Enum.Parse(
                    typeof(SortType),
                    (string)moduleSettings["SortType"]);
            }
            catch (Exception)
            {
                this.settings.CloudSortType = SortType.AlphabeticAsc;
            }

            var sRenderUl = (string)moduleSettings["RenderUl"];

            this.settings.RenderUl = !string.IsNullOrEmpty(sRenderUl) && bool.Parse(sRenderUl);

            if (this.settings.RenderMode.Equals(RenderMode.Html5))
            {
                this.settings.RenderUl = true;
                this.c1.CanvasEnabled = true;
            }

            this.c1.Skin = this.settings.SkinName;

            // Fix for sliding theme
            if (this.settings.SkinName.Contains("Sliding"))
            {
                this.settings.RenderUl = true;
            }

            this.c1.RenderItemWeight = this.settings.RenderItemWeight;

            var strTagsLink = (string)moduleSettings["tagslink"];

            this.settings.TagsLink = string.IsNullOrEmpty(strTagsLink) || bool.Parse(strTagsLink);

            var strTagsLinkChk = (string)moduleSettings["tagslinkChk"];

            this.settings.TagsLinkChk = string.IsNullOrEmpty(strTagsLinkChk) || bool.Parse(strTagsLinkChk);

            var sCacheItems = (string)moduleSettings["CacheItems"];

            this.settings.CacheItems = string.IsNullOrEmpty(sCacheItems) || bool.Parse(sCacheItems);

            try
            {
                this.settings.FlashWidth = (string)moduleSettings["flashwidth"];
            }
            finally
            {
                if (string.IsNullOrEmpty(this.settings.FlashWidth))
                {
                    this.settings.FlashWidth = "500";
                }
            }

            try
            {
                this.settings.FlashHeight = (string)moduleSettings["flashheight"];
            }
            finally
            {
                if (string.IsNullOrEmpty(this.settings.FlashHeight))
                {
                    this.settings.FlashHeight = "300";
                }
            }

            try
            {
                this.settings.TagCloudWidth = (string)moduleSettings["tagcloudwidth"];
            }
            finally
            {
                if (string.IsNullOrEmpty(this.settings.TagCloudWidth))
                {
                    this.settings.TagCloudWidth = "500";
                    this.settings.TagCloudWValue = "px";
                }

                if (this.settings.TagCloudWidth.Contains("px"))
                {
                    this.settings.TagCloudWidth = this.settings.TagCloudWidth.Replace("px", string.Empty);
                    this.settings.TagCloudWValue = "px";
                }

                if (this.settings.TagCloudWidth.Contains("%"))
                {
                    this.settings.TagCloudWidth = this.settings.TagCloudWidth.Replace("%", string.Empty);
                    this.settings.TagCloudWValue = "%";
                }
            }

            if (this.settings.TagCloudWidth.Equals("-1"))
            {
                this.settings.TagCloudWidth = "auto";
                this.settings.TagCloudWValue = string.Empty;
            }

            this.settings.Tcolor = $"0x{TagCloudController.NormalizeColor((string)moduleSettings["tcolor"], "000000")}";
            this.settings.Hicolor = $"0x{TagCloudController.NormalizeColor((string)moduleSettings["hicolor"], "42a5ff")}";

            var bgColor = TagCloudController.NormalizeColor(
                (string)moduleSettings["bgcolor"],
                TagCloudController.Transparent);

            this.settings.Bgcolor = bgColor == TagCloudController.Transparent ? bgColor : $"#{bgColor}";

            // Load World Cloud Settings only when needed
            if (this.settings.RenderMode.Equals(RenderMode.WordCloud))
            {
                // Word Cloud Shape Setting
                if (!string.IsNullOrEmpty((string)moduleSettings["Shape"]))
                {
                    try
                    {
                        this.settings.WordCloudSettings.Shape = (WordCloudShape)Enum.Parse(
                            typeof(WordCloudShape),
                            (string)moduleSettings["Shape"]);
                    }
                    catch (Exception)
                    {
                        this.settings.WordCloudSettings.Shape = WordCloudShape.circle;
                    }
                }
                else
                {
                    this.settings.WordCloudSettings.Shape = WordCloudShape.circle;
                }

                // World Cloud GridSize Setting
                this.settings.WordCloudSettings.GridSize = ParseInt((string)moduleSettings["GridSize"], 8);

                // World Cloud Ellipticity Setting
                this.settings.WordCloudSettings.Ellipticity =
                    ParseDouble((string)moduleSettings["Ellipticity"], 0.65);

                // World Cloud WeightFactor Setting
                this.settings.WordCloudSettings.WeightFactor =
                    ParseDouble((string)moduleSettings["WeightFactor"], 2.1);

                // World Cloud MinSize Setting
                this.settings.WordCloudSettings.MinSize = ParseInt((string)moduleSettings["MinSize"], 0);

                // World Cloud FillBox Setting
                if (!string.IsNullOrEmpty((string)moduleSettings["FillBox"]))
                {
                    try
                    {
                        this.settings.WordCloudSettings.FillBox = bool.Parse((string)moduleSettings["FillBox"]);
                    }
                    catch (Exception)
                    {
                        this.settings.WordCloudSettings.FillBox = false;
                    }
                }
                else
                {
                    this.settings.WordCloudSettings.FillBox = false;
                }
            }

            // Set TagCloud width & Height to Main DIV Tag
            this.tagCloudDiv.Attributes["style"] =
                $"width:{this.settings.TagCloudWidth}{this.settings.TagCloudWValue};height:auto;";
        }
    }
}