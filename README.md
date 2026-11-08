<h1 align="center">
  <picture>
    <source media="(prefers-color-scheme: dark)" srcset=".github/images/TagCloudLogo-dark.png">
    <img src="Source/Resources/TagCloudLogo.png" alt="TagCloud" width="455">
  </picture>
</h1>

<p align="center">
  <a href="https://github.com/w8tcha/TagCloud/releases/latest"><img src="https://img.shields.io/github/v/release/w8tcha/TagCloud" alt="Latest release"></a>
  <a href="LICENSE.md"><img src="https://img.shields.io/github/license/w8tcha/TagCloud" alt="License"></a>
  <img src="https://img.shields.io/badge/DNN-10.4.1%2B-blue" alt="DNN 10.4.1+">
</p>

A tag cloud module for [DNN](https://www.dnnsoftware.com/) (formerly DotNetNuke). It displays tags from various sources as an animated 3D cloud, a "Wordle" style word cloud or a skinnable HTML tag cloud, where each tag is styled according to its weight. Clicking a tag opens the portal search with all pages for that tag.

| 3D Canvas Cloud | Word Cloud |
| :---: | :---: |
| ![3D canvas tag cloud](screenshots/TagCloudCanvas.jpg) | ![Word cloud](screenshots/dnnTagCloudWordle.png) |

## Features

### Render modes

- **HTML5 Canvas (3D)**: a rotating 3D sphere of tags that reacts to the mouse. Font, text color, highlight color, background color (or transparent) and size are configurable. Tag weights can be shown by size, colour or both, with an adjustable size multiplier.
- **Word Cloud**: a "Wordle" inspired cloud drawn on a canvas. You can choose the shape (circle, cardioid, diamond, triangle, pentagon or star), grid size, ellipticity, weight factor, minimum font size and whether gaps between words may be filled.
- **Basic HTML**: a list of links styled with CSS. It ships with 18 skins, can be rendered as an unordered list (`<ul>`) or plain links with a custom separator, and can optionally show the weight next to each tag.

The canvas and word cloud renderers are plain JavaScript and do not require jQuery.

### Tag sources

Several sources can be combined:

- **Custom tags**: your own list of tags, each with a weight and URL. Tags can be localized per language and imported from or exported to XML.
- **DNN taxonomy**: terms of all pages or only of the current page, limited to selected vocabularies.
- **Ventrian News Articles**, **Ventrian Simple Gallery** and **Active Forums**: tags of these modules. The sources only appear when the module is installed.

### Filtering and sorting

- Maximum number of tags and minimum tag weight (occurrence)
- Sort by name or weight, ascending or descending, randomly or not at all
- Exclude common words of the current language (configurable in the language files)
- Exclude your own words, matching tags that equal, contain, start with or end with the word

### Links and search

- Tags link to the portal search. The search page and the query strings for normal and taxonomy searches are configurable.
- Optionally hide tags whose target page the current user is not allowed to view.

### More

- Caching of the generated tags for faster page loads
- Import and export of the module settings, custom tags including their translations and exclusion words (`IPortable`), e.g. for page and site templates
- Localized in English, German and Polish

## Requirements

- DNN 10.4.1 or later
- .NET Framework 4.8.1

## Installation

1. Download the latest `TagCloud_xx.xx.xx.zip` package from the [releases page](https://github.com/w8tcha/TagCloud/releases).
2. In DNN, go to **Settings > Extensions**, click **Install Extension** and upload the package.
3. Add the **TagCloud** module to a page and open **Manage Cloud** from the module actions to choose the render mode, tag sources and appearance.

Upgrading works the same way: install the new package over the existing one, and your settings are kept.

## Creating your own skin

The HTML render mode is styled by the CSS files in `DesktopModules/TagCloud/Skins`. Every `.css` file in this folder shows up in the skin list of the module settings, so to add a skin:

1. Copy an existing skin, e.g. `Standard.css`, to `MySkin.css`.
2. Rename the class prefix in the file from `Standard` to `MySkin`:
   - `.TagCloud-MySkin` is the container of the cloud
   - `.CloudItem-MySkin_0` to `.CloudItem-MySkin_6` are the tags, from the lowest to the highest weight
3. Select **MySkin** in the module settings.

## Localization

The texts are stored in the resource files in `App_LocalResources`:

- `Settings.ascx.resx`: texts of the settings page
- `TagCloud.ascx.resx`: texts of the module, including the comma separated list of common words to exclude (`CommonWords.Text`)

To add a language, copy both files with the culture code in the name (e.g. `Settings.ascx.fr-FR.resx`) and translate the values, or use the language editor in DNN.

## Building from source

Requirements: Visual Studio 2022 or later with the .NET Framework 4.8.1 developer pack.

- Open `TagCloud.slnx` to work on the module.
- Run `BuildPackages.bat`, or `msbuild Source/TagCloud.csproj /restore /p:Configuration=Deploy`, to create the install package in the `deploy` folder.

Releases are created automatically: when the version in `Source/Installation/TagCloud.dnn` is raised and pushed to `master`, a GitHub Actions workflow builds the package and publishes it as a new release.

## Screenshots

| | |
| :---: | :---: |
| ![TagTastic skin](screenshots/TagCloud-TagTastic-Skin.jpg)<br>TagTastic skin | ![RoundedButton skin](screenshots/TagCloud-RoundedButton-Skin.jpg)<br>RoundedButton skin |
| ![Orange skin](screenshots/TagCloudSkinOrange.jpg)<br>Orange skin | ![Purple skin](screenshots/TagCloudSkinPurple.jpg)<br>Purple skin |
| ![Rotated skin](screenshots/TagCloudSkinRotated.jpg)<br>Rotated skin | ![Tag source settings](screenshots/TagCloudSourceSettings.jpg)<br>Tag source settings |
| ![Exclude settings](screenshots/TagCloudExludes.jpg)<br>Exclude settings | ![Custom tags export dialog](screenshots/TagCloudExportDialog.jpg)<br>Custom tags export dialog |

## Credits

- [TagCanvas](https://www.goat1000.com/tagcanvas.php) by Graham Breach (LGPL-3.0), used for the 3D canvas cloud
- The word cloud renderer is based on [wordcloud](https://github.com/timdream/wordcloud) by Tim Chien

## License

TagCloud is licensed under the [BSD 2-Clause License](LICENSE.md).
