# Перенос ассоциативного преобразования защиты данных на GPU в .NET: материалы

[English version](README.md)

Материалы к статье *GPU Offloading of the Associative Data Protection Transform in .NET: A Performance Evaluation*
(Р. Ф. Гибадуллин, Д. С. Георгиев; на рецензировании): результаты бенчмарков конфигурации A, код библиотеки и её
бенчмарков, программа замеров конфигураций B и C с исходными результатами и входными данными, набор замеров
конфигурации D с отчётами и журналами BenchmarkDotNet, отчёты тестов NIST SP 800-22 и скрипты, по которым
пересчитываются таблицы и рисунки статьи.

> **В статье оценивается производительность, а не стойкость.** Ничто в этом репозитории не обосновывает стойкость
> преобразования. Функция заполнителя в ядрах GPU — бесключевой хэш открытых индексов и 32-битной затравки порции;
> прохождение тестов NIST SP 800-22 не делает её криптографическим генератором.

## Состав

| Путь | Содержимое | В статье |
|---|---|---|
| [`library/`](library/) | Листинги библиотеки CpuGpuStego и бенчмарков BenchmarkDotNet конфигурации A; соответствие имён классов в коде и в статье | разд. 4–5, 6.2, 6.4 |
| [`results/configuration_A/`](results/configuration_A/) | Сводные отчёты BenchmarkDotNet конфигурации A (изображения) и их значения (JSON, CSV) | табл. 4–6, рис. 2–3, разд. 7.2–7.4, 7.11, табл. S1 |
| [`measurement/AssocMeasure/`](measurement/AssocMeasure/) | Консольная программа на .NET 8 для замеров CPU-части на конфигурациях B и C | разд. 5.2–5.3, 6.3–6.6 |
| [`measurement/run_linux.sh`](measurement/run_linux.sh), [`run_windows.cmd`](measurement/run_windows.cmd) | Скрипты, выполняющие весь набор этих замеров | |
| [`measurement/laptop/`](measurement/laptop/) | Набор замеров конфигурации D: классы BenchmarkDotNet и замеры одних ядер для компьютера с GPU, с самостоятельной сборкой | разд. 5.3, 6.4, 7.8–7.11 |
| [`data/`](data/) | Шесть входных файлов по 10 МБ для конфигураций B и C (четыре из них использованы и на конфигурации D), их контрольные суммы и происхождение | разд. 6.3 |
| [`results/configuration_B/`](results/configuration_B/), [`results/configuration_C/`](results/configuration_C/) | Исходные результаты по итерациям, журналы, проверки ключей, отчёты NIST SP 800-22 | табл. 7–9, рис. 4, разд. 7.1, 7.5–7.7, табл. S2–S4 |
| [`results/configuration_D/`](results/configuration_D/) | Отчёты и журналы BenchmarkDotNet со временами всех итераций каждого процесса, времена одних ядер, сведения о среде | табл. 10–15, рис. 5, разд. 7.8–7.11, табл. S5–S6 |
| [`results/Supplementary_Tables_S1-S6.xlsx`](results/Supplementary_Tables_S1-S6.xlsx) | Таблицы S1–S6, на которые ссылается статья: полная статистика конфигураций A и D, замеры B и C с итерациями, парный дизайн, результаты NIST, времена ядер на D | разд. 6.7 |
| [`analysis/`](analysis/) | Скрипты на Python, пересчитывающие табл. 4–15 и рис. 2–5 по `results/` | |

## Конфигурации

| | A | B | C | D |
|---|---|---|---|---|
| Назначение | Бенчмарки CPU и GPU | Анализ CPU-части, проверка ключей, тесты заполнителя | Анализ CPU-части, тест системного генератора | Бенчмарки CPU и GPU, добавленные при доработке |
| Компьютер | Ноутбук Acer Nitro V 15 | Виртуальная машина в облаке | Ноутбук HP OMEN 15-dc1xxx | Ноутбук ASUS ROG Strix SCAR 18 (G834JYR) |
| Процессор | Intel Core i5-13420H (4P + 4E ядер, 12 потоков) | 2 vCPU процессора Intel Xeon (AVX-512, AES-NI) | Intel Core i5-9300H (4 ядра, 8 потоков) | Intel Core i9-14900HX (8P + 16E ядер, 32 потока) |
| Память | 16 ГБ DDR5-5200 | 8 ГБ | 16 ГБ DDR4-2666 | 32 ГБ |
| GPU | Intel UHD Graphics (OpenCL), NVIDIA GeForce RTX 4050 Laptop GPU (CUDA) | нет | не использовались | Intel UHD Graphics (OpenCL), NVIDIA GeForce RTX 4090 Laptop GPU (CUDA) |
| ПО | Windows 11 25H2, .NET 7.0.16, BenchmarkDotNet 0.15.8, ILGPU 1.5.3 | Ubuntu 24.04.5, .NET 8.0.32, OpenSSL 3.0.13 | Windows 11 (сборка 26300), .NET 8.0.33 | Windows 11 Home 25H2, .NET 8.0.33, BenchmarkDotNet 0.15.8, ILGPU 1.5.3 |

Полное описание — в табл. 3 статьи. Замеры GPU на конфигурации A выполнены классом бенчмарков из
`library/StegoBenchmarks.cs`, на конфигурациях B и C — программой `measurement/AssocMeasure`, на конфигурации D —
набором `measurement/laptop`, который компилирует листинг библиотеки из `library/` без изменений.

## Пересчёт таблиц и рисунков

Python 3.10 или новее с NumPy и Matplotlib (`pip install -r analysis/requirements.txt`):

```
python3 analysis/config_a.py        # табл. 4–6 и производные величины конфигурации A
python3 analysis/summarize.py B     # конфигурация B: формирование, распознавание, парный дизайн, NIST
python3 analysis/summarize.py C     # конфигурация C
python3 analysis/config_d.py        # конфигурация D: табл. 10–15, S5, S6; сверка журналов с отчётами
python3 analysis/paper_values.py    # табл. 4–15 и величины из текста -> analysis/output/paper_values.md
python3 analysis/figures.py         # рис. 2–5 -> analysis/output/
```

`analysis/run_all.sh` выполняет все шесть команд. Подробнее — [`analysis/README.md`](analysis/README.md).

## Повторение замеров CPU

При установленном .NET 8 SDK скрипт `measurement/run_linux.sh` (Linux) или `measurement\run_windows.cmd` (Windows)
собирает программу, выполняет весь набор замеров конфигураций B и C (около часа) и записывает результаты в
`results_run/`. Программа не использует пакетов NuGet и GPU. Команды и параметры —
[`measurement/README.md`](measurement/README.md). Тесты NIST SP 800-22 запускаются скриптом `analysis/run_nist.sh`;
опубликованные последовательности заполнителя GPU воспроизводятся по затравкам
(`results/configuration_B/filler_seeds.txt`).

## Повторение замеров GPU конфигурации D

В Windows при установленном .NET 8 SDK и наличии GPU команда
`measurement\laptop\run_revision.cmd <текстовый файл> <репозиторий>\data` собирает набор самостоятельно (ILGPU и
BenchmarkDotNet из NuGet), выполняет шесть его классов BenchmarkDotNet и замер одних ядер и собирает результаты в
`results_revision\` (около 45 минут на конфигурации D). Команды, группы и параметры —
[`measurement/laptop/README_ru.md`](measurement/laptop/README_ru.md); пошаговая инструкция —
[`RUN_INSTRUCTIONS_RU.md`](measurement/laptop/RUN_INSTRUCTIONS_RU.md).

## Что не включено

* Входные тексты конфигурации A: файлы UTF-8 объёмом от 1 КБ до 100 МБ, полученные из романа О. Селянкина
  «Когда труба зовёт», и текст книги объёмом 3,45 МБ. Они охраняются авторским правом; в
  [`data/README.md`](data/README.md) указаны размер и SHA-256 файла романа и скрипт, вычисляющий приведённые для него
  характеристики.
* Собираемый проект библиотеки: листинги взяты из приложений магистерской диссертации, на которой основана статья;
  в них нет класса `BitVector` и файлов проекта (см. [`library/README.md`](library/README.md)).
* Отчёты об оборудовании с серийными номерами, имена пользователей и локальные пути: пути в отчётах NIST заменены
  относительными.

## Лицензия

Код, данные и тексты авторов в этом репозитории распространяются по [лицензии MIT](LICENSE). Материалы третьих лиц
сохраняют свои условия: `data/en_10MB.bin` (тексты лицензий и сведения об авторских правах из Ubuntu) и
`data/zip_10MB.bin` (фрагменты сборок .NET по лицензии MIT .NET Foundation, `data/LICENSE-dotnet.txt`); см.
[`data/README.md`](data/README.md).

## Цитирование

При использовании материалов ссылайтесь на статью (ссылка будет добавлена после публикации).
