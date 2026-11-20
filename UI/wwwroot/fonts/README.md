# Фирменные шрифты 4iT

Положите сюда файлы шрифта Engravers Gothic (лицензионный, в Google Fonts отсутствует):

- `EngraversGothic.woff2`
- `EngraversGothic.woff`

Шрифт подключается в `wwwroot/app.css` через `@font-face`. Если файлов нет,
заголовки отображаются шрифтом Montserrat в верхнем регистре с увеличенным
межбуквенным интервалом.

Проверьте, что выбранная версия Engravers Gothic содержит кириллицу —
заголовки интерфейса на русском языке.

Montserrat (Light 300, Medium 500) подключается из Google Fonts в `Components/App.razor`.
Для работы без интернета скачайте его и подключите локально через `@font-face`.
