define(['jQuery', 'loading', 'globalize', 'dom', 'emby-select', 'emby-button', 'emby-input', 'listViewStyle'], function ($, loading, globalize, dom) {
    'use strict';

    window.MovieDbWithProxyPage = {
        onSubmit: function (e) {
            var form = this;

            var page = dom.parentWithClass(form, 'page');

            ApiClient.getNamedConfiguration("moviedbwithproxy").then(function (config) {
                config.ProxyType = $('#selectProxyType', page).val();
                config.ProxyUrl = page.querySelector('#txtProxyUrl').value;
                config.ProxyPort = page.querySelector('#txtProxyPort').value;
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
                page.querySelector('#txtProxyUrl').value = config.ProxyUrl || '';
                page.querySelector('#txtProxyPort').value = config.ProxyPort || '';
                
                loading.hide();

            });
        });
    };
});
