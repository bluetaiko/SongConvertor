# SongConverter

JA: https://github.com/bluetaiko/SongConvertor/blob/main/README.md

EN: https://github.com/bluetaiko/SongConvertor/blob/main/README_EN.md

## How to use SongSorter
Copies TJA files from esegit and organizes them in the same order as the official game.
1. In the **AddSongs** tab, select the parent folder where you want to download songs and click the "Add Songs" button. (If Git is not installed, please install it from https://git-scm.com/)
2. Select the destination **Songs** folder.
3. Click the **Start Sorting** button.

## How to use DanGenerator
Creates Dan (Dan-dojo) files compatible with TaikøNauts, etc., using information from the Taiko Wiki Dan-dojo page.
1. Enter the URL of the Taiko Wiki Dan-dojo page. (e.g., https://wikiwiki.jp/taiko-fumen/%E6%AE%B5%E4%BD%8D%E9%81%93%E5%A0%B4)
2. Decide the name of the output folder.
3. Select the **Songs** folder.
4. Click the **Generate Dan** button.

## How to use DanConvertor
Converts TJA Dan files (from OpenTaiko, etc.) into Dan files compatible with TaikøNauts, etc.
1. Select the TJA file(s) you want to convert.
2. Decide the name of the output folder.
3. Select the **Songs** folder (Optional).
4. Click the **Convert** button.

*Note: Specifying difficulty automatically was technically difficult due to the TJA structure. Please manually specify the difficulty by changing the "difficulty" value in "danSongs". (0: Easy, 1: Normal, 2: Hard, 3: Oni, 4: Ura-Oni)*
By default, as long as you don't select a simulation folder, the Dan TJA will be split so that you don't have to specify the difficulty manually.
