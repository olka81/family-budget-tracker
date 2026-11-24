// The browser's local date as "yyyy-MM-dd". The server only knows the UTC date, which is
// a different day around midnight. Called from Blazor pages through JS interop.
window.familyBudget = window.familyBudget || {};

window.familyBudget.getLocalDate = function () {
    var now = new Date();
    var year = String(now.getFullYear()).padStart(4, '0');
    var month = String(now.getMonth() + 1).padStart(2, '0');
    var day = String(now.getDate()).padStart(2, '0');
    return year + '-' + month + '-' + day;
};
