document.addEventListener('DOMContentLoaded', function () {
    document.querySelectorAll('input[type="password"]').forEach(function (input) {
        var toggle = document.createElement('label');
        toggle.style.display = 'block';
        toggle.style.marginBottom = '1rem';

        var checkbox = document.createElement('input');
        checkbox.type = 'checkbox';
        checkbox.addEventListener('change', function () {
            input.type = checkbox.checked ? 'text' : 'password';
        });

        toggle.appendChild(checkbox);
        toggle.appendChild(document.createTextNode(' Show password'));

        var anchor = input.closest('.form-floating') || input;
        anchor.insertAdjacentElement('afterend', toggle);
    });
});