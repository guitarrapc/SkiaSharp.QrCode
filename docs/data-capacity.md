# Data Capacity Reference

The actual capacity depends on the QR code type, encoding mode, ECC level, and version. Below are **practical capacities** including overhead (based on test data using 'あ' for UTF-8 multi-byte characters).

## Standard QR Code (Versions 1-40)

### Quick Reference (Version 10, Common Use Cases)

| ECC Level | Numeric | Alphanumeric | Byte (ASCII) | Byte (UTF-8 Multi-byte*) |
|-----------|---------|--------------|--------------|--------------------------|
| L | 652 | 395 | ~270 | 90 |
| M | 513 | 311 | ~210 | 70 |
| Q | 364 | 221 | ~150 | 50 |
| H | 288 | 174 | ~117 | 39 |

> UTF-8 multi-byte: Japanese hiragana 'あ' (3 bytes/char). For ASCII text (1 byte/char), the capacity is approximately 3× the values shown.

**Full capacity tables** for all Standard QR Code versions (1-40) and ECC levels are available in the [Data Capacity Tables](#data-capacity-tables) section below.

### Data Capacity Tables

Full capacity tables for all Standard QR Code versions and ECC levels.

**Test Characters:**
- Numeric: `'1'` (digit)
- Alphanumeric: `'A'` (uppercase letter)
- Byte (ASCII): `'a'` (lowercase, 1 byte)
- Byte (UTF-8): `'あ'` (hiragana, 3 bytes)

**Important Notes:**
- **Numeric**: Pure digit count (0-9)
- **Alphanumeric**: Uppercase letters, digits, and symbols (45 character set)
- **Byte**: UTF-8 encoded Japanese characters (ひらがな 'あ' = 3 bytes per character)
  - For ASCII characters (1 byte each), multiply the byte value by ~3
  - For theoretical byte capacity, refer to ISO/IEC 18004 Table 7

#### ECC Level: L

<details><summary>Click to expand full capacity tables</summary>

| Version | Numeric | Alphanumeric | Byte (UTF-8 Multi-byte*) |
|---------|---------|--------------|------|
|  1 |      41 |           25 |    5 |
|  2 |      77 |           47 |   10 |
|  3 |     127 |           77 |   17 |
|  4 |     187 |          114 |   25 |
|  5 |     255 |          154 |   35 |
|  6 |     322 |          195 |   44 |
|  7 |     370 |          224 |   51 |
|  8 |     461 |          279 |   63 |
|  9 |     552 |          335 |   76 |
| 10 |     652 |          395 |   90 |
| 11 |     772 |          468 |  106 |
| 12 |     883 |          535 |  122 |
| 13 |    1022 |          619 |  141 |
| 14 |    1101 |          667 |  152 |
| 15 |    1250 |          758 |  173 |
| 16 |    1408 |          854 |  195 |
| 17 |    1548 |          938 |  214 |
| 18 |    1725 |         1046 |  239 |
| 19 |    1903 |         1153 |  263 |
| 20 |    2061 |         1249 |  285 |
| 21 |    2232 |         1352 |  309 |
| 22 |    2409 |         1460 |  334 |
| 23 |    2620 |         1588 |  363 |
| 24 |    2812 |         1704 |  390 |
| 25 |    3057 |         1853 |  424 |
| 26 |    3283 |         1990 |  455 |
| 27 |    3517 |         2132 |  488 |
| 28 |    3669 |         2223 |  509 |
| 29 |    3909 |         2369 |  542 |
| 30 |    4158 |         2520 |  577 |
| 31 |    4417 |         2677 |  613 |
| 32 |    4686 |         2840 |  650 |
| 33 |    4965 |         3009 |  689 |
| 34 |    5253 |         3183 |  729 |
| 35 |    5529 |         3351 |  767 |
| 36 |    5836 |         3537 |  810 |
| 37 |    6153 |         3729 |  854 |
| 38 |    6479 |         3927 |  899 |
| 39 |    6743 |         4087 |  936 |
| 40 |    7089 |         4296 |  984 |

</details>

#### ECC Level: M

<details>
<summary>Click to expand full capacity tables</summary>

| Version | Numeric | Alphanumeric | Byte (UTF-8 Multi-byte*) |
|---------|---------|--------------|------|
|  1 |      34 |           20 |    4 |
|  2 |      63 |           38 |    8 |
|  3 |     101 |           61 |   13 |
|  4 |     149 |           90 |   20 |
|  5 |     202 |          122 |   27 |
|  6 |     255 |          154 |   35 |
|  7 |     293 |          178 |   40 |
|  8 |     365 |          221 |   50 |
|  9 |     432 |          262 |   59 |
| 10 |     513 |          311 |   70 |
| 11 |     604 |          366 |   83 |
| 12 |     691 |          419 |   95 |
| 13 |     796 |          483 |  110 |
| 14 |     871 |          528 |  120 |
| 15 |     991 |          600 |  137 |
| 16 |    1082 |          656 |  149 |
| 17 |    1212 |          734 |  167 |
| 18 |    1346 |          816 |  186 |
| 19 |    1500 |          909 |  207 |
| 20 |    1600 |          970 |  221 |
| 21 |    1708 |         1035 |  236 |
| 22 |    1872 |         1134 |  259 |
| 23 |    2059 |         1248 |  285 |
| 24 |    2188 |         1326 |  303 |
| 25 |    2395 |         1451 |  332 |
| 26 |    2544 |         1542 |  352 |
| 27 |    2701 |         1637 |  374 |
| 28 |    2857 |         1732 |  396 |
| 29 |    3035 |         1839 |  421 |
| 30 |    3289 |         1994 |  456 |
| 31 |    3486 |         2113 |  483 |
| 32 |    3693 |         2238 |  512 |
| 33 |    3909 |         2369 |  542 |
| 34 |    4134 |         2506 |  573 |
| 35 |    4343 |         2632 |  602 |
| 36 |    4588 |         2780 |  636 |
| 37 |    4775 |         2894 |  662 |
| 38 |    5039 |         3054 |  699 |
| 39 |    5313 |         3220 |  737 |
| 40 |    5596 |         3391 |  776 |

</details>

#### ECC Level: Q

<details>
<summary>Click to expand full capacity tables</summary>

| Version | Numeric | Alphanumeric | Byte (UTF-8 Multi-byte*) |
|---------|---------|--------------|------|
|  1 |      27 |           16 |    3 |
|  2 |      48 |           29 |    6 |
|  3 |      77 |           47 |   10 |
|  4 |     111 |           67 |   15 |
|  5 |     144 |           87 |   19 |
|  6 |     178 |          108 |   24 |
|  7 |     207 |          125 |   28 |
|  8 |     259 |          157 |   35 |
|  9 |     312 |          189 |   43 |
| 10 |     364 |          221 |   50 |
| 11 |     427 |          259 |   58 |
| 12 |     489 |          296 |   67 |
| 13 |     580 |          352 |   80 |
| 14 |     621 |          376 |   85 |
| 15 |     703 |          426 |   97 |
| 16 |     775 |          470 |  107 |
| 17 |     876 |          531 |  121 |
| 18 |     948 |          574 |  131 |
| 19 |    1063 |          644 |  147 |
| 20 |    1159 |          702 |  160 |
| 21 |    1224 |          742 |  169 |
| 22 |    1358 |          823 |  188 |
| 23 |    1468 |          890 |  203 |
| 24 |    1588 |          963 |  220 |
| 25 |    1718 |         1041 |  238 |
| 26 |    1804 |         1094 |  250 |
| 27 |    1933 |         1172 |  268 |
| 28 |    2085 |         1263 |  289 |
| 29 |    2181 |         1322 |  302 |
| 30 |    2358 |         1429 |  327 |
| 31 |    2473 |         1499 |  343 |
| 32 |    2670 |         1618 |  370 |
| 33 |    2805 |         1700 |  389 |
| 34 |    2949 |         1787 |  409 |
| 35 |    3081 |         1867 |  427 |
| 36 |    3244 |         1966 |  450 |
| 37 |    3417 |         2071 |  474 |
| 38 |    3599 |         2181 |  499 |
| 39 |    3791 |         2298 |  526 |
| 40 |    3993 |         2420 |  554 |

</details>

#### ECC Level: H

<details>
<summary>Click to expand full capacity tables</summary>

| Version | Numeric | Alphanumeric | Byte (UTF-8 Multi-byte*) |
|---------|---------|--------------|------|
|  1 |      17 |           10 |    2 |
|  2 |      34 |           20 |    4 |
|  3 |      58 |           35 |    7 |
|  4 |      82 |           50 |   11 |
|  5 |     106 |           64 |   14 |
|  6 |     139 |           84 |   19 |
|  7 |     154 |           93 |   21 |
|  8 |     202 |          122 |   27 |
|  9 |     235 |          143 |   32 |
| 10 |     288 |          174 |   39 |
| 11 |     331 |          200 |   45 |
| 12 |     374 |          227 |   51 |
| 13 |     427 |          259 |   58 |
| 14 |     468 |          283 |   64 |
| 15 |     530 |          321 |   73 |
| 16 |     602 |          365 |   83 |
| 17 |     674 |          408 |   93 |
| 18 |     746 |          452 |  103 |
| 19 |     813 |          493 |  112 |
| 20 |     919 |          557 |  127 |
| 21 |     969 |          587 |  134 |
| 22 |    1056 |          640 |  146 |
| 23 |    1108 |          672 |  153 |
| 24 |    1228 |          744 |  170 |
| 25 |    1286 |          779 |  178 |
| 26 |    1425 |          864 |  197 |
| 27 |    1501 |          910 |  208 |
| 28 |    1581 |          958 |  219 |
| 29 |    1677 |         1016 |  232 |
| 30 |    1782 |         1080 |  247 |
| 31 |    1897 |         1150 |  263 |
| 32 |    2022 |         1226 |  280 |
| 33 |    2157 |         1307 |  299 |
| 34 |    2301 |         1394 |  319 |
| 35 |    2361 |         1431 |  327 |
| 36 |    2524 |         1530 |  350 |
| 37 |    2625 |         1591 |  364 |
| 38 |    2735 |         1658 |  379 |
| 39 |    2927 |         1774 |  406 |
| 40 |    3057 |         1852 |  424 |

</details>

## Micro QR Code (M1-M4)

Character capacities per version and ECC level (ISO/IEC 18004 Table 7; `—` = mode or ECC level not available on that version):

| Version | ECC | Numeric | Alphanumeric | Byte |
|---------|-----|---------|--------------|------|
| M1 (11×11) | Detection only | 5 | — | — |
| M2 (13×13) | L | 10 | 6 | — |
| M2 (13×13) | M | 8 | 5 | — |
| M3 (15×15) | L | 23 | 14 | 9 |
| M3 (15×15) | M | 18 | 11 | 7 |
| M4 (17×17) | L | 35 | 21 | 15 |
| M4 (17×17) | M | 30 | 18 | 13 |
| M4 (17×17) | Q | 21 | 13 | 9 |

Byte capacities are encoded byte counts: ISO-8859-1 text costs 1 byte per character, UTF-8 multi-byte text costs its UTF-8 length (e.g. 'あ' = 3 bytes).

## rMQR Code (R7x43-R17x139)

Character capacities per version and ECC level (ISO/IEC 23941, verified against external encoders; `R{height}x{width}` in modules, quiet zone excluded). rMQR supports ECC levels M and H only.

| Version | M: Numeric | M: Alphanumeric | M: Byte | H: Numeric | H: Alphanumeric | H: Byte |
|---------|-----------:|----------------:|--------:|-----------:|----------------:|--------:|
| R7x43 | 12 | 7 | 5 | 5 | 3 | 2 |
| R7x59 | 26 | 16 | 11 | 14 | 8 | 6 |
| R7x77 | 45 | 27 | 19 | 21 | 13 | 9 |
| R7x99 | 64 | 39 | 27 | 30 | 18 | 13 |
| R7x139 | 102 | 62 | 42 | 54 | 33 | 22 |
| R9x43 | 26 | 16 | 11 | 14 | 8 | 6 |
| R9x59 | 47 | 29 | 20 | 23 | 14 | 10 |
| R9x77 | 71 | 43 | 30 | 37 | 23 | 16 |
| R9x99 | 97 | 59 | 40 | 49 | 30 | 20 |
| R9x139 | 147 | 89 | 61 | 75 | 46 | 31 |
| R11x27 | 14 | 8 | 6 | 9 | 6 | 4 |
| R11x43 | 42 | 26 | 18 | 23 | 14 | 10 |
| R11x59 | 71 | 43 | 30 | 33 | 20 | 14 |
| R11x77 | 100 | 60 | 41 | 52 | 31 | 21 |
| R11x99 | 133 | 81 | 55 | 66 | 40 | 27 |
| R11x139 | 198 | 120 | 82 | 97 | 59 | 40 |
| R13x27 | 26 | 16 | 11 | 14 | 8 | 6 |
| R13x43 | 62 | 37 | 26 | 28 | 17 | 12 |
| R13x59 | 88 | 53 | 36 | 45 | 27 | 18 |
| R13x77 | 124 | 75 | 51 | 66 | 40 | 27 |
| R13x99 | 171 | 104 | 71 | 80 | 49 | 33 |
| R13x139 | 251 | 152 | 104 | 126 | 76 | 52 |
| R15x43 | 76 | 46 | 31 | 33 | 20 | 13 |
| R15x59 | 112 | 68 | 46 | 59 | 36 | 24 |
| R15x77 | 157 | 95 | 65 | 71 | 43 | 29 |
| R15x99 | 207 | 126 | 86 | 111 | 68 | 46 |
| R15x139 | 301 | 182 | 125 | 162 | 98 | 67 |
| R17x43 | 90 | 55 | 37 | 47 | 28 | 19 |
| R17x59 | 131 | 79 | 54 | 63 | 38 | 26 |
| R17x77 | 183 | 111 | 76 | 87 | 53 | 36 |
| R17x99 | 236 | 143 | 98 | 131 | 79 | 54 |
| R17x139 | 361 | 219 | 150 | 178 | 108 | 74 |

Byte capacities are encoded byte counts (ISO-8859-1 text 1 byte per character, UTF-8 multi-byte text its UTF-8 length). Automatic version selection picks the fewest-modules symbol by default (`RmQRFitStrategy.MinimizeArea`, the same choice other encoders make), which can be taller and narrower than the flattest fit; use `RmQRFitStrategy.MinimizeHeight` or a fixed `RmQRHeight` for label lanes.

The table above is per single mode, which is what the default `RmQRSegmentation.Single` encodes. Content that mixes modes is not bound by any single row: `RmQRSegmentation.Optimal` splits it into the runs that cost the fewest bits, so `https://example.com/p/1234567890123456` (38 characters, over the 37 Byte-mode characters R17x43-M holds) fits R15x43 as a Byte run plus a Numeric run. Mixed content therefore has no single capacity number, only a bit budget: `8 × data codewords`, spent as `3 + count indicator + payload` per run plus an 11-bit ECI prefix when one is emitted.
