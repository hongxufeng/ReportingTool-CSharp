<%@ Page Language="C#" MasterPageFile="~/headers/M1.Master" AutoEventWireup="true" CodeBehind="ReportingTool.aspx.cs" Inherits="IMSROOT.ReportingTool.ReportingTool" %>

<%@ Register TagPrefix="uc0" TagName="NavBtn" Src="~/headers/EdmHeader.ascx" %>
<%@ Register TagPrefix="uc1" TagName="leftbar" Src="~/ims_blocks/growthblocks2.ascx" %>

<asp:Content ContentPlaceHolderID="Title" runat="server">成绩统计</asp:Content>
<asp:Content ContentPlaceHolderID="JS" runat="server"><script src="js/CourseList.js" type="text/javascript"></script></asp:Content>
<asp:Content ContentPlaceHolderID="Heading" runat="server">教学资源管理</asp:Content>
<asp:Content ContentPlaceHolderID="NavButton" runat="server"><uc0:NavBtn runat="server" /></asp:Content>
<asp:Content ContentPlaceHolderID="LeftSide" runat="server"><uc1:leftbar runat="server" /></asp:Content>
<asp:Content ContentPlaceHolderID="RightSide" runat="server"><div id="gradeTable"></div></asp:Content>
<asp:Content ContentPlaceHolderID="Script" runat="server">
    <script type="text/javascript">
        $(document).ready(function () {
            var rtCallback = function () {
                $("#gradeTable").find('[data-value="-1"]').html("未判分");
            }
            $("#gradeTable").rt({ complete: rtCallback, configFile: "Grade", table: "课程概况", navBar: true, rowList: [50, 100, 150, 200] });
        });
    </script>
</asp:Content>