# com.avmedvedskiy.scrspy

Пакет для Unity Editor для работы с Android-устройствами:
- установка APK на подключенные устройства
- запуск/остановка шаринга экрана и записи сессии через `scrcpy`
- сбор логов устройства через `adb`
- создание и отправка баг-репортов в Jira

## Пункты меню

- `Tools/BuildTools/Android/Open Devices Window`
- `Tools/BuildTools/Android/Install Selected APK And Run scrcpy`

## Основной сценарий

1. Откройте **Devices Window**.
2. Выберите APK (опционально, для сценария установки).
3. Укажите **Bundle Name** (автоматически восстанавливается из настроек, при установке может быть считан из APK).
4. Запустите сессию на устройстве (`Start Session`).
5. Остановите сессию (`Stop Session`) после завершения.
6. Откройте окно баг-репорта (`Stop And Report`) и заполните поля Jira.
7. Отправьте задачу в Jira с вложениями.

## Выходные данные сессии

Данные сессии сохраняются рядом с `Assets` проекта в:

`SessionCaptures/<DeviceName>/<yyyy-MM-dd_HH-mm-ss>/`

В папке могут появляться файлы:
- `log.txt`
- `log_unity.txt` (если есть)
- `video.mkv`
- `summary.txt`
- `screenshot*.png`

## Окно Bug Report

Окно поддерживает:
- ввод настроек и учетных данных Jira
- заголовок и описание бага
- предпросмотр скриншота и ручную замену скриншота
- AI-кнопку (`Make AI`) для анализа логов

При отправке пакет создает задачу Jira и прикрепляет файлы сессии.
`summary.txt` во вложения не добавляется.

## Требования

- Unity Editor (пакет используется в Editor)
- Android-устройство с включенной USB-отладкой
- `adb` и `scrcpy` из папки `scrspy` внутри пакета
- доступ к Jira Cloud (Base URL, project key/name, email, API key)
- локально установленный `codex` CLI (опционально, для AI-функций)
