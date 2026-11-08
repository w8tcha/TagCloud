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
using System.Linq;
using System.Text;
using System.Xml;

using DotNetNuke.Common;
using DotNetNuke.Common.Utilities;
using DotNetNuke.Entities.Modules;
using DotNetNuke.Services.Exceptions;
using DotNetNuke.Services.Localization;

namespace DNN.Modules
{
    /// <summary>
    /// The controller.
    /// </summary>
    public class TagCloudController : IPortable
    {
        /// <summary>
        /// The setting value for a transparent background.
        /// </summary>
        internal const string Transparent = "transparent";

        /// <summary>
        /// How long the generated tags are cached.
        /// </summary>
        internal static readonly TimeSpan ItemsCacheDuration = TimeSpan.FromMinutes(15);

        /// <summary>
        /// Gets the cache key for the generated tags of a module instance in a language.
        /// </summary>
        /// <param name="tabModuleId">The tab module id.</param>
        /// <param name="cultureCode">The culture code, e.g. "de-DE".</param>
        /// <returns>The cache key.</returns>
        internal static string GetItemsCacheKey(int tabModuleId, string cultureCode)
        {
            return $"TagCloud_Items_{tabModuleId}_{cultureCode}";
        }

        /// <summary>
        /// Removes the cached tags of a module instance in all languages of the portal.
        /// </summary>
        /// <param name="tabModuleId">The tab module id.</param>
        /// <param name="portalId">The portal id.</param>
        internal static void ClearItemsCache(int tabModuleId, int portalId)
        {
            foreach (var cultureCode in LocaleController.Instance.GetLocales(portalId).Keys)
            {
                DataCache.RemoveCache(GetItemsCacheKey(tabModuleId, cultureCode));
            }

            // Cache key used by older versions
            DataCache.RemoveCache($"CloudItems{tabModuleId}");
        }

        /// <summary>
        /// Normalizes a color value to the stored format: a 6 digit lower case hex value without
        /// prefix (e.g. "42a5ff") or "transparent". Accepts values with "0x" or "#" prefix and
        /// 3 digit hex values, as written by older exports.
        /// </summary>
        /// <param name="value">The color value.</param>
        /// <param name="defaultValue">The value returned if the color is missing or invalid.</param>
        /// <returns>The normalized color.</returns>
        internal static string NormalizeColor(string value, string defaultValue)
        {
            var color = (value ?? string.Empty).Trim();

            if (color.Equals(Transparent, StringComparison.OrdinalIgnoreCase)
                || color.Equals("#" + Transparent, StringComparison.OrdinalIgnoreCase))
            {
                return Transparent;
            }

            if (color.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                color = color.Substring(2);
            }
            else if (color.StartsWith("#", StringComparison.Ordinal))
            {
                color = color.Substring(1);
            }

            if (color.Length == 3)
            {
                color = string.Concat(color[0], color[0], color[1], color[1], color[2], color[2]);
            }

            if (color.Length != 6 || !color.All(Uri.IsHexDigit))
            {
                return defaultValue;
            }

            return color.ToLowerInvariant();
        }

        /// <summary>
        /// The tab module settings that are exported and imported, the XML element name is the setting name.
        /// Element names of older versions are imported case insensitive or via <see cref="LegacyElementNames"/>.
        /// </summary>
        private static readonly string[] PortableSettings =
            {
                "RenderMode", "SkinName", "TagSeparator", "RenderUl", "RenderItemWeight", "occurcount", "tagscount",
                "SortType", "ExcludeCommon", "tagslink", "tagslinkChk", "CacheItems", "CustomSearchPage",
                "SearchPageQueryString", "SearchPageTaxQueryString", "tagcloudwidth", "flashwidth", "flashheight",
                "FontFamily", "WeightMode", "WeightSize", "tcolor", "hicolor", "bgcolor", "Ellipticity", "GridSize",
                "MinSize", "Shape", "WeightFactor", "FillBox", "ModeCustom", "ModeTax", "ModeNewsarticles",
                "ModeSimplegallery", "ModeActiveforums", "TaxMode", "TaxVocabularies", "NewsArticlesTab",
                "NewsArticlesModule", "SimpleGalleryTab", "SimpleGalleryModule", "ActiveForumsTab",
                "ActiveForumsModule"
            };

        /// <summary>
        /// Element names written (or expected) by older versions that differ from the setting name.
        /// </summary>
        private static readonly Dictionary<string, string[]> LegacyElementNames =
            new Dictionary<string, string[]>
                {
                    { "NewsArticlesTab", new[] { "newsarcticletab", "newsarticlestab" } },
                    { "NewsArticlesModule", new[] { "newsarcticlemodule", "newsarticlesmodule" } },
                    { "TaxVocabularies", new[] { "vocabulary" } }
                };

        /// <summary>
        /// Export Module Settings
        /// </summary>
        /// <param name="moduleID">
        ///  Current Module Instance id
        /// </param>
        /// <returns>
        /// The export module.
        /// </returns>
        public string ExportModule(int moduleID)
        {
            try
            {
                var tabModule = ModuleController.Instance.GetTabModulesByModule(moduleID)
                    .FirstOrDefault(module => !module.IsDeleted);

                if (tabModule == null)
                {
                    return string.Empty;
                }

                var moduleSettings = tabModule.TabModuleSettings;

                var sBXml = new StringBuilder();

                sBXml.AppendLine("<TagCloud>");
                sBXml.AppendLine("<Settings>");

                foreach (var name in PortableSettings)
                {
                    // Only export settings that are set, missing settings keep the defaults on import
                    if (!(moduleSettings[name] is string value))
                    {
                        continue;
                    }

                    sBXml.AppendFormat("<{0}>{1}</{0}>", name, XmlUtils.XMLEncode(NormalizeSetting(name, value)));
                }

                sBXml.AppendLine("</Settings>");
                sBXml.AppendLine("</TagCloud>");

                return sBXml.ToString();
            }
            catch (Exception exc)
            {
                Exceptions.LogException(exc);

                return string.Empty;
            }
        }

        /// <summary>
        /// Import Module Settings
        /// </summary>
        /// <param name="moduleID">
        ///  The Module Id
        /// </param>
        /// <param name="content">
        ///  The Content
        /// </param>
        /// <param name="version">
        ///  The Version
        /// </param>
        /// <param name="userID">
        ///  The User ID
        /// </param>
        public void ImportModule(int moduleID, string content, string version, int userID)
        {
            try
            {
                var xmlTagCloud = Globals.GetContent(content, "TagCloud");

                if (!(xmlTagCloud?.SelectSingleNode("Settings") is XmlNode xmlSettings))
                {
                    return;
                }

                // Element names are matched case insensitive, older versions used different casing
                var elements = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

                foreach (XmlNode node in xmlSettings.ChildNodes)
                {
                    if (node.NodeType == XmlNodeType.Element && !elements.ContainsKey(node.Name))
                    {
                        elements.Add(node.Name, node.InnerText);
                    }
                }

                var tabModules = ModuleController.Instance.GetTabModulesByModule(moduleID)
                    .Where(module => !module.IsDeleted)
                    .ToList();

                foreach (var name in PortableSettings)
                {
                    if (!TryGetElement(elements, name, out var value))
                    {
                        continue;
                    }

                    value = NormalizeSetting(name, value);

                    foreach (var tabModule in tabModules)
                    {
                        ModuleController.Instance.UpdateTabModuleSetting(tabModule.TabModuleID, name, value);
                    }
                }

                foreach (var tabModule in tabModules)
                {
                    // Remove the cached tags, so the imported settings are used right away
                    ClearItemsCache(tabModule.TabModuleID, tabModule.PortalID);
                }
            }
            catch (Exception exc)
            {
                Exceptions.LogException(exc);
            }
        }

        /// <summary>
        /// Gets the value of a setting element, including element names of older versions.
        /// </summary>
        /// <param name="elements">The imported elements by name.</param>
        /// <param name="name">The setting name.</param>
        /// <param name="value">The element value.</param>
        /// <returns>True if the element exists.</returns>
        private static bool TryGetElement(IDictionary<string, string> elements, string name, out string value)
        {
            if (elements.TryGetValue(name, out value))
            {
                return true;
            }

            if (LegacyElementNames.TryGetValue(name, out var legacyNames))
            {
                foreach (var legacyName in legacyNames)
                {
                    if (elements.TryGetValue(legacyName, out value))
                    {
                        return true;
                    }
                }
            }

            value = null;

            return false;
        }

        /// <summary>
        /// Normalizes the setting values that need a fixed format.
        /// </summary>
        /// <param name="name">The setting name.</param>
        /// <param name="value">The setting value.</param>
        /// <returns>The normalized value.</returns>
        private static string NormalizeSetting(string name, string value)
        {
            switch (name)
            {
                case "tcolor":
                    return NormalizeColor(value, "000000");
                case "hicolor":
                    return NormalizeColor(value, "42a5ff");
                case "bgcolor":
                    return NormalizeColor(value, Transparent);
                default:
                    return value;
            }
        }
    }
}