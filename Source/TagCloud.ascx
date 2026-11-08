<%@ Control Language="c#" AutoEventWireup="True" Codebehind="TagCloud.ascx.cs" Inherits="DNN.Modules.TagCloud" %>

<%@ Register TagPrefix="vrk" Namespace="DNN.Modules.VRK.Controls" Assembly="TagCloud" %>

<asp:Panel id="tagCloudDiv" runat="server" CssClass="TagCloud">
     <vrk:Cloud id="c1" runat="server" ItemCssClassPrefix="CloudItem" DataTextField="Text" DataHrefField="Href" DataTitleField="Title" DataWeightField="Weight">
    <Items></Items>
  </vrk:Cloud>
</asp:Panel>
<div style="clear:both"></div>