define(['jQuery', 'loading', 'globalize', 'dom', 'emby-select', 'emby-button', 'emby-input', 'listViewStyle'], function ($, loading, globalize, dom) {
    'use strict';

    window.MovieDbWithProxyPage = {
        onSubmit: function (e) {

            loading.show();

            var form = this;

            var page = dom.parentWithClass(form, 'page');

            ApiClient.getNamedConfiguration("moviedbwithproxy").then(function (config) {
                config.ProxyType = $('#selectProxyType', page).val();
                config.ProxyUrl = page.querySelector('#txtProxyUrl').value;
                config.ProxyPort = page.querySelector('#txtProxyPort').value;

                var port = parseInt(config.ProxyPort, 10);
                if (port && port > 1 && port < 65536) {
                    ApiClient.updateNamedConfiguration("moviedbwithproxy", config)
                        .then(Dashboard.processServerConfigurationUpdateResult);
                } else {
                    loading.hide();
                }
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
                page.querySelector('#txtProxyUrl').value = config.ProxyUrl || '';
                page.querySelector('#txtProxyPort').value = config.ProxyPort || '';
                
                loading.hide();

            });
        });
    };
});
