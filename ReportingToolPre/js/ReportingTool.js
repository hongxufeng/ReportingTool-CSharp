(function ($) {

    var cachedRows = {};

    $.fn.rt = function (options) {

        this.html("<div class=\"rt-table\"></div>");
        var _this = this, globalVars = { orderby: "", xsc: "DESC", condition: "", tabfilter: "", initUrl: "", url: "", rd: "" };

        var settings = $.extend({
            complete: function () { },
            configFile: "",
            navBar: false,
            rowList: [15, 30, 60, 100],
            searchBar: true,
            searchComplete: function () { },
            searchBarOnShow: function () { },
            searchBarOnHide: function () { },
            style: "table",
            table: "",
            tab: false,
            queryStr: location.href.indexOf("?") === -1 ? "1=1" : location.href.substring(location.href.indexOf("?") + 1)
        }, options);

        var load = function () {
            var condition = $("input[data-mark='rt-condition']:hidden");
            if (condition.length > 0) {
                condition.each(function () {
                    globalVars.condition += "&" + $(this).attr("name") + "=" + encodeURI($(this).val());
                });
            }
            globalVars.initUrl = "/ReportingToolPre/Handler.ashx?" + settings.queryStr + globalVars.condition;
            if (settings.tab === true) {
                $.ajax({
                    type: 'POST',
                    url: globalVars.initUrl,
                    data: { cmd: "getTab", configFile: settings.configFile, table: settings.table },
                    success: function (data, status) {
                        var jsonObject = jQuery.parseJSON(data);
                        if (!jsonObject.exception) {
                            _this.prepend(jsonObject.tab);
                            var defalttab = _this.find(".rt-tab-span").eq(0);
                            var tabquery = "&" + defalttab.attr("data-name") + "=" + encodeURI(defalttab.attr("data-value"));
                            globalVars.tabfilter += tabquery;
                            defalttab.parent().addClass("active");
                        }
                    },
                    async: false
                });
            }
            globalVars.initUrl += globalVars.tabfilter;
            globalVars.url = globalVars.initUrl;
            postData();
        }
        var postData = function (elem) {
            $.post(globalVars.url, { cmd: "getTable", configFile: settings.configFile, table: settings.table, orderby: "", rowList: settings.rowList.toString(), page: 1, rows: settings.rowList[0], colPage: 1 }, function (data, status) {
                //var jsonObject = JSON.parse(data);
                var jsonObject = jQuery.parseJSON(data);
                globalVars.rd = Math.random().toString().substring(2);
                if (settings.searchBar === true) {
                    _this.prepend("<div class=\"rt-searchBar\">\
                                            <div id=\"rt-search-" + globalVars.rd + "\" class=\"rt-search\"></div>\
                                            <span class=\"glyphicon glyphicon-remove rt-btn-hideSb\"></span>\
                                            <button class=\"btn btn-danger btn-sm rt-btn-clean\">清&nbsp;&nbsp;&nbsp;空</button>\
                                            <button class=\"btn btn-info btn-sm rt-btn-search\">查&nbsp;&nbsp;&nbsp;询</button>\
                                        </div>");
                    $("#rt-search-" + globalVars.rd).html(jsonObject.search);
                }
                _this.find(".rt-table").html(jsonObject.table);
                if (settings.navBar === true) {
                    var nb = _this.find(".rt-nav");
                    if (!nb.length) {
                        _this.prepend("<ol class=\"rt-nav breadcrumb\"></ol>");
                    }
                    if (!jsonObject.exception) {
                        _this.find(".rt-nav").append("<li><a class='rt-nav-a' href='#' data-url='" + globalVars.url + "' data-tablename='" + $(elem).attr("data-table") + "'>" + $(elem).attr("data-navname") + "</a></li>");
                    }
                    if (elem === undefined) {
                        _this.find(".rt-nav").find("a").eq(0).attr("data-tablename", settings.table).html(settings.table);
                    }
                    _this.find(".rt-nav").css("visibility", "visible");
                }
                _this.off();
                _this.on("click", "[data-table]", getTable).on("click", ".rt-nav-a", getTableByNav);
                _this.on("click", ".rt-btn-search", search).on("click", ".rt-btn-clean", clean).on("click", ".rt-btn-hideSb", hideSearchBar);
                _this.on("click", "th", sort).on("click", ".rt-pager-search", showSearchBar);
                _this.on("click", ".rt-pager-firstPage", firstPage).on("click", ".rt-pager-lastPage", lastPage);
                _this.on("click", ".rt-pager-prevPage", prevPage).on("click", ".rt-pager-nextPage", nextPage);
                _this.on("click", ".rt-colPager-prev", prevCols).on("click", ".rt-colPager-next", nextCols);
                _this.on("keypress", ".rt-pager-page", page_Keypress).on("change", ".rt-pager-rowList", rowList_Change);
                _this.on("mouseover", "tbody tr", trOnMouseover).on("mouseout", "tbody tr", trOnMouseout);
                _this.on("click", "tbody tr", trOnClick);
                _this.on("click", ".rt-th-checkbox>.rt-checkboxWrapper", toggleAll);
//                _this.on("keyup", ".rt-search-txt", startSearching);
//                _this.on("change", ".rt-search-txt.date", startSearching);
                _this.on("click", ".rt-pager-export", exportExcel);
                _this.on("click", ".rt-tab-span", tabClick)
                cachedRows = {}
                settings.complete();
            });
        }
        var conditionChange = function () {
            var condition = $("input[data-mark='rt-condition']:hidden");
            if (condition.length > 0) {
                globalVars.condition = "";
                condition.each(function () {
                    globalVars.condition += "&" + $(this).attr("name") + "=" + encodeURI($(this).val());
                });
            }
        }
        var getTable = function () {
            var elem = this;
            globalVars.initUrl = "/ReportingToolPre/Handler.ashx?" + $(this).attr("data-passedcol");
            globalVars.url = globalVars.initUrl;
            settings.table = $(this).attr("data-table");
            postData(elem);
        }
        var getTableByNav = function () {
            var elem = this;
            globalVars.initUrl = $(this).attr("data-url");
            globalVars.url = globalVars.initUrl;
            settings.table = $(this).attr("data-tablename");
            $.post(globalVars.url, { cmd: "getTable", configFile: settings.configFile, table: settings.table, orderby: "", rowList: settings.rowList.toString(), page: 1, rows: settings.rowList[0], colPage: 1 }, function (data, status) {
                var jsonObject = JSON.parse(data);
                if (settings.searchBar === true) {
                    $("#rt-search-" + globalVars.rd).html(jsonObject["search"]);
                }
                if (settings.tab === true) {
                    var defalttab = _this.find(".rt-tab-span").eq(0);
                    defalttab.parent().siblings("li").removeClass("active");
                    defalttab.parent().addClass("active");
                }
                _this.find(".rt-table").html(jsonObject["table"]);
                $(elem).parent().nextAll().remove();
                settings.complete();
            });
        }
        var refresh = function () {
            $.post(globalVars.url, { cmd: "getTable", configFile: settings.configFile, table: settings.table, orderby: globalVars.orderby, xsc: globalVars.xsc, rowList: settings.rowList.toString(), page: _this.find(".rt-pager-page").val(), rows: _this.find(".rt-pager-rowList").val(), colPage: _this.find(".rt-colPager-page").val() }, function (data, status) {
                var jsonObject = JSON.parse(data);
                _this.find(".rt-table").html(jsonObject["table"]);
                settings.complete();
                $("td .rt-checkboxWrapper").each(function () {
                    var checkbox = $(this).find(".rt-checkbox");
                    if (cachedRows[checkbox.val()]) {
                        checkbox[0].checked = true;
                        $(this).addClass("checked");
                        $(this).closest("tr").addClass("rt-tr-selected")
                    }
                });
                var hasCheckbox = _this.find("td .rt-checkbox").length;
                if (hasCheckbox && hasCheckbox === _this.find("td .rt-checkbox:checked").length) {
                    var thCheckbox = _this.find(".rt-th-checkbox");
                    thCheckbox.find(".rt-checkboxWrapper").addClass("checked");
                    thCheckbox.find(".rt-checkbox")[0].checked = true;
                }
            });
        }
        var search = function () {
            var queryStr = "";
            conditionChange();
            $("#rt-search-" + globalVars.rd).find(".rt-search-txt").each(function () {
                if ($(this).val() != "") { queryStr += "&" + $(this).attr("name") + "=" + encodeURI($(this).val()); }
            });
            globalVars.initUrl = "/ReportingToolPre/Handler.ashx?" + settings.queryStr + globalVars.condition + globalVars.tabfilter;
            globalVars.url = globalVars.initUrl + queryStr;
            _this.find(".rt-pager-page").val(1);
            refresh();
            settings.searchComplete();
            return false;
        }
//        var startSearching = function () {
//            setTimeout(search, 1);
//        }
        var clean = function () {
            $(".rt-search-txt").each(function () {
                $(this).val("");
            });
            search();
            return false;
        }
        var showSearchBar = function () {
            var searchDiv = $("#rt-search-" + globalVars.rd);
            var table = _this;
            var elem = searchDiv.parent(".rt-searchBar-shown");
            if (elem.length) {
                searchDiv.parent(".rt-searchBar").removeClass("rt-searchBar-shown");
                settings.searchBarOnHide();
            }
            else {
                searchDiv.parent(".rt-searchBar").addClass("rt-searchBar-shown");
                settings.searchBarOnShow();
                $(".rt-search-txt.date").datepicker({
                    format: "yy/mm/dd", weekStart: 1, language: "zh-CN", orientation: "bottom left", keyboardNavigation: false, autoclose: true, todayHighlight: true
                });
            }
        }
        var hideSearchBar = function () {
            var searchDiv = $("#rt-search-" + globalVars.rd);
            var elem = searchDiv.parent(".rt-searchBar-shown");
            if (elem.length) {
                searchDiv.parent(".rt-searchBar").removeClass("rt-searchBar-shown");
                settings.searchBarOnHide();
            }
        }
        var sort = function () {
            if ($(this).html() !== "操作" && !$(this).hasClass("rt-th-checkbox")) {
                if (globalVars.xsc === "DESC") {
                    globalVars.xsc = "ASC";
                }
                else {
                    globalVars.xsc = "DESC";
                }
                globalVars.orderby = $(this).attr("name");
                refresh();
            }
        }
        var firstPage = function () {
            _this.find(".rt-pager-page").val("1");
            refresh();
        }
        var prevPage = function () {
            var pageNumber = Number(_this.find(".rt-pager-page").val());
            if (pageNumber > 1) {
                _this.find(".rt-pager-page").val(pageNumber - 1);
                refresh();
            }
        }
        var nextPage = function () {
            var pageNumber = Number(_this.find(".rt-pager-page").val());
            var totalPage = Number(_this.find(".rt-pager-totalPages").html());
            if (pageNumber < totalPage) {
                _this.find(".rt-pager-page").val(pageNumber + 1);
                refresh();
            }
        }
        var lastPage = function () {
            _this.find(".rt-pager-page").val(_this.find(".rt-pager-totalPages").html());
            refresh();
        }
        var page_Keypress = function () {
            if (event.keyCode === 13) {
                var pageNumber = parseInt(_this.find(".rt-pager-page").val());
                var totalPage = Number(_this.find(".rt-pager-totalPages").html());
                if (isNaN(pageNumber) || pageNumber < 1) {
                    pageNumber = 1;
                }
                else if (pageNumber > totalPage) {
                    pageNumber = totalPage
                }
                _this.find(".rt-pager-page").val(pageNumber);
                refresh();
            }
        }
        var rowList_Change = function () {
            var pageNumber = parseInt(_this.find(".rt-pager-page").val());
            var rowsPerPage = parseInt($(this).val());
            var totalRecords = parseInt(_this.find(".rt-pager-totalRecords").html());
            if (pageNumber * rowsPerPage > totalRecords) {
                var x = ~ ~(totalRecords / rowsPerPage);
                var y = totalRecords % rowsPerPage == 0 ? 0 : 1;
                _this.find(".rt-pager-page").val(x + y);
            }
            refresh();
        }
        var prevCols = function () {
            var pageNumber = Number(_this.find(".rt-colPager-page").val());
            if (pageNumber > 1) {
                _this.find(".rt-colPager-page").val(pageNumber - 1);
                refresh();
            }
        }
        var nextCols = function () {
            var pageNumber = Number(_this.find(".rt-colPager-page").val());
            var totalPage = Number(_this.find(".rt-colPager-totalColPages").val());
            if (pageNumber < totalPage) {
                _this.find(".rt-colPager-page").val(pageNumber + 1);
                refresh();
            }
        }
        var tabClick = function () {
            globalVars.tabfilter = "&" + $(this).attr("data-name") + "=" + encodeURI($(this).attr("data-value"));
            globalVars.initUrl = "/ReportingToolPre/Handler.ashx?" + settings.queryStr + globalVars.condition + globalVars.tabfilter;
            globalVars.url = globalVars.initUrl;
            refresh();
            $(this).parent().siblings("li").removeClass("active");
            $(this).parent().addClass("active");
            return false;
        }
        var trOnMouseover = function () {
            $(this).addClass("rt-tr-onhover");
        }
        var trOnMouseout = function () {
            $(this).removeClass("rt-tr-onhover");
        }
        var trOnClick = function () {
            var elem = $(this).find(".rt-td-checkbox");
            if (elem.length) {
                if ($(this).hasClass("rt-tr-selected")) {
                    $(this).removeClass("rt-tr-selected");
                    elem.find(".rt-checkboxWrapper").removeClass("checked");
                    var checkbox = elem.find(".rt-checkbox");
                    if (checkbox.length) {
                        checkbox[0].checked = false;
                        var rowID = checkbox.val();
                        delete cachedRows[rowID];
                        var thCheckbox = _this.find(".rt-th-checkbox");
                        thCheckbox.find(".rt-checkboxWrapper").removeClass("checked");
                        thCheckbox.find(".rt-checkbox")[0].checked = false;
                    }
                }
                else {
                    $(this).addClass("rt-tr-selected");
                    elem.find(".rt-checkboxWrapper").addClass("checked");
                    var checkbox = elem.find(".rt-checkbox");
                    if (checkbox.length) {
                        checkbox[0].checked = true;
                        var rowid = checkbox.val();
                        if (!cachedRows[rowid]) {
                            var rowObj = {};
                            var cells = $(this).find("td");
                            cells.each(function () {
                                rowObj[$(this).attr("name")] = $(this).attr("data-value");
                            });
                            cachedRows[rowid] = rowObj;
                        }
                        if (_this.find("td .rt-checkbox").length === _this.find("td .rt-checkbox:checked").length) {
                            var thCheckbox = _this.find(".rt-th-checkbox");
                            thCheckbox.find(".rt-checkboxWrapper").addClass("checked");
                            thCheckbox.find(".rt-checkbox")[0].checked = true;
                        }
                    }
                }
            }
            else {
                var cells = $(this).find("td");
                if ($(this).hasClass("rt-tr-selected")) {
                    $(this).removeClass("rt-tr-selected");
                    var rowid = cells.eq(0).attr("data-value");
                    delete cachedRows[rowid];
                }
                else {
                    cachedRows = {};
                    var selected = _this.find(".rt-tr-selected");
                    if (selected.length) {
                        selected.removeClass("rt-tr-selected");
                    }
                    $(this).addClass("rt-tr-selected");
                    var rowid = cells.eq(0).attr("data-value");
                    if (!cachedRows[rowid]) {
                        var rowObj = {};
                        cells.each(function () {
                            rowObj[$(this).attr("name")] = $(this).attr("data-value");
                        });
                        cachedRows[rowid] = rowObj;
                    }
                }
            }
        }
        var toggleAll = function () {
            var allCheckbox = _this.find("td .rt-checkboxWrapper");
            var hasChecked = $(this).hasClass("checked");
            if (hasChecked) {
                $(this).removeClass("checked");
                $(this).find(".rt-checkbox")[0].checked = false;
                _this.find(".rt-tr-selected").removeClass("rt-tr-selected");
                allCheckbox.removeClass("checked");
                allCheckbox.each(function () {
                    var checkbox = $(this).find(".rt-checkbox");
                    checkbox[0].checked = false;
                    var rowID = checkbox.val();
                    delete cachedRows[rowID];
                });
            }
            else {
                $(this).addClass("checked");
                $(this).find(".rt-checkbox")[0].checked = true;
                _this.find("tbody tr").addClass("rt-tr-selected");
                allCheckbox.addClass("checked");
                allCheckbox.each(function () {
                    var checkbox = $(this).find(".rt-checkbox");
                    checkbox[0].checked = true;
                    var rowid = checkbox.val();
                    if (!cachedRows[rowid]) {
                        var rowObj = {};
                        var cells = $(this).closest("tr").find("td");
                        cells.each(function () {
                            rowObj[$(this).attr("name")] = $(this).attr("data-value");
                        });
                        cachedRows[rowid] = rowObj;
                    }
                });
            }
        }
        var exportExcel = function () {
            var exportUrl = "&" + globalVars.url.substring(globalVars.url.indexOf("?") + 1);
            window.open("/ReportingToolPre/GenerateExcel.aspx?table=" + settings.table + "&ConfigFile=" + settings.configFile + "&orderby=" + globalVars.orderby + "&xsc=" + globalVars.xsc + exportUrl);
        }

        load();
    }

    $.fn.rtGetCheckedRows = function () {
        return cachedRows;
    }

} (jQuery));