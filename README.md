<div align="center">

<img src="docs/readme-banner.png" alt="ITBackup — ручной бэкап 1С" width="100%"/>

<br/>

[![Release](https://img.shields.io/github/v/release/SlayerSS/ITService.Backup?style=for-the-badge)](https://github.com/SlayerSS/ITService.Backup/releases/latest)
[![Windows](https://img.shields.io/badge/Windows-10%2B-0078D4?style=for-the-badge&logo=windows)](https://github.com/SlayerSS/ITService.Backup/releases/latest)
[![License](https://img.shields.io/badge/лицензия-Fork%20·%20NC-orange?style=for-the-badge)](LICENSE)

### Простая ручная бэкапилка для **1С**

Подготовили носитель → отметили, что копировать → **«Сделать резервную копию»** → готово.  
Без расписаний и фоновых служб — только когда вы сами решили сделать бэкап.

[Скачать](https://github.com/SlayerSS/ITService.Backup/releases/latest) · [it.nojabrsk.info](https://it.nojabrsk.info/) · **8 961 552-52-52**

</div>

---

## Зачем

**ITBackup** — для администратора или бухгалтера на сервере с **1С на SQL Server**: быстро сохранить базы и папки 1С перед обновлением, переездом или «на всякий случай».

| | |
|:--|:--|
| **Главное** | Бэкап баз **1С** (`.bak` через SQL Server) |
| **Дополнительно** | Файловые базы и папки 1С |
| **Как** | Одна кнопка, вручную, когда нужно вам |
| **Куда** | USB-флешка, диск или папка: `Backups\дата\` |

---

## Три шага

1. **Скачать** exe с [Releases](https://github.com/SlayerSS/ITService.Backup/releases/latest) и запустить **от администратора**.
2. **Подготовить место для архива** — обычно USB (в настройках можно указать диск или папку на этом ПК).
3. **Отметить галочками**, что копировать, и нажать **«Сделать резервную копию»**.

При первом запуске программа **сама находит** на этом компьютере SQL Server, базы и пути 1С из списка пользователя. Лишнее снимите галочкой. В **настройках** — сервер SQL, папки, куда сохранять, сжатие ZIP.

---

## Какую сборку скачать

| Файл | .NET на сервере |
|:-----|:----------------|
| **ITBackup-net8.exe** | [.NET 8 Desktop](https://dotnet.microsoft.com/download/dotnet/8.0) |
| **ITBackup-standalone.exe** | не нужен |

Конфиг и логи: `%ProgramData%\ITBackup\`.

Windows **x64**, 10/11 или Server 2016+.

---

## Что умеет (коротко)

- Поиск **SQL Server**, баз и **файловых каталогов 1С** на этом ПК
- Сохранение на **USB**, **любой диск** или в **папку**
- Переключение между **несколькими флешками**, если их несколько
- Оценка **свободного места** перед копированием
- **`.bak`** для SQL без поломки цепочки штатных бэкапов SQL
- Защита от **случайной перезаписи** готовой папки архива

Подробнее: **[docs/USAGE.md](docs/USAGE.md)**

---

## Безопасность

- В **1С/SQL** — только резервное копирование
- **Исходные** данные не перезаписываются — запись только в каталог архива
- Настройки: `%ProgramData%\ITBackup\`

---

## IT Service

| | |
|:--|:--|
| Сайт | [it.nojabrsk.info](https://it.nojabrsk.info/) |
| Телефон | [8 961 552-52-52](tel:+79615525252) |
| Город | Ноябрьск |

---

<details>
<summary><b>Разработчикам: сборка</b></summary>

```powershell
git clone https://github.com/SlayerSS/ITService.Backup.git
cd ITService.Backup
dotnet build -c Release

.\publish.ps1                 # ITBackup-net8.exe
.\publish.ps1 -SelfContained  # ITBackup-standalone.exe
```

Текст для страницы релиза: `docs/RELEASE_TEMPLATE.md`

</details>

## Лицензия

Исходники: **[форк на GitHub, без коммерции](LICENSE)**. Официальные **Releases** от IT Service — для работы у клиентов.  
Коммерческое использование кода — только с согласия IT Service.

<sub>© IT Service, Ноябрьск</sub>
