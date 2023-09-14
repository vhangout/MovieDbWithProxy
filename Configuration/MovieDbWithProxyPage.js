define(['jQuery', 'loading', 'globalize', 'dom', 'emby-select', 'emby-button', 'emby-input', 'listViewStyle'], function ($, loading, globalize, dom) {
    'use strict';

    window.MovieDbWithProxyPage = {
        onSubmit: function (e) {
            var form = this;

            var page = dom.parentWithClass(form, 'page');

            ApiClient.getNamedConfiguration("moviedbwithproxy").then(function (config) {
                config.Enable = form.querySelector('#chkEnableProxy').checked;
                config.ProxyType = $('#selectProxyType', page).val();
                config.ProxyUrl = page.querySelector('#txtProxyUrl').value;
                config.ProxyPort = page.querySelector('#txtProxyPort').value;

                config.EnableCredentials = false; // form.querySelector('#chkEnableCredentials').checked;                
                //config.Login = page.querySelector('#txtLogin').value;
                //config.Password = page.querySelector('#txtPassword').value;

                config.EnableDebugLog = form.querySelector('#chkEnableDebugLogging').checked;
                ApiClient.updateNamedConfiguration("moviedbwithproxy", config)
                    .then(Dashboard.processServerConfigurationUpdateResult,
                        function (response) {
                            response.text().then(text => Dashboard.alert({message: text}));
                        });
            });

            e.preventDefault();
            e.stopPropagation();
            return false;
        },
    };

    return function (view, params) {

        var page = view; 

        $('.movieDbWithProxyForm').on('submit', MovieDbWithProxyPage.onSubmit);

        view.addEventListener('viewshow', function () {

            loading.show();

            var page = this;

            ApiClient.getNamedConfiguration("moviedbwithproxy").then(function (config) {
                $('#selectProxyType', page).val(config.ProxyType || '').change();                
                page.querySelector('#chkEnableProxy').checked = config.Enable;
                page.querySelector('#txtProxyUrl').value = config.ProxyUrl || '';
                page.querySelector('#txtProxyPort').value = config.ProxyPort || '';
                //page.querySelector('#chkEnableCredentials').checked = config.EnableCredentials;
                //page.querySelector('#txtLogin').value = config.Login || '';
                //page.querySelector('#txtPassword').value = config.Password || '';
                page.querySelector('#chkEnableDebugLogging').checked = config.EnableDebugLog;
                loading.hide();
            });
        });
    };
});
