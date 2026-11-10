# English translation glossary

Binding terminology for all translators (human or LLM). Keep names and terms consistent across UI,
items and dialogue. When the reference translation (`ref`) disagrees with this glossary, the glossary wins.

## Style
- American English spelling. Item names in Title Case; descriptions and dialogue in normal sentence case.
- Keep it concise for UI (menu labels, prompts, item names): the UI boxes are sized for short Japanese.
- Item descriptions: literary, slightly archaic tone matching the dark-fantasy setting (Souls-like flavor text).
- Dialogue: natural spoken English; keep stutters, moans and sound effects short and readable
  (e.g. 「あ・・・っ」 → "Ah...!"). Do not over-explain or add content that is not there.
- Replace Japanese ellipses `・・・` / `…` with `...`; full-width punctuation with ASCII (`！`→`!`, `？`→`?`, `：`→`:`).
- Keep line breaks (`\n`) roughly where they are; long lines may be rebalanced but never add more than one extra line.
- **Preserve all markup exactly**: TextMeshPro tags such as `<size=70%>`, `<color=#ff0000>`, `</size>`, and any
  `{0}`-style placeholders, numbers and key names.
- Fragments: some code strings are pieces that the game concatenates with a name or number
  (e.g. `<item>を習得した` → the literal is only `を習得した`). Translate so the result reads naturally
  when appended: `を習得した` → ` acquired.` (leading space), `：` → `: `.

## Policies (user decisions)
- Write censored words out uncensored (ま〇こ → pussy, チ〇ポ → dick, ペ〇ス → penis).
- Names stay as close to the original as possible: romanize, never translate the meaning of a name.
- No Japanese honorifics (-san, -kun, -chan, -sama, -senpai): the setting is European. Use English forms
  ("Miss Aradia", "Sir", "Lady", "Sister") or the bare name, depending on politeness.

## Names
| Japanese | English |
|---|---|
| アラディア | Aradia |
| 茨の魔女 | Witch in the Thorns (the hero/boss title; "Sword of the Witch in the Thorns") |
| スズネ | Suzune |
| 魔女の里 | Witch Village |

## Core terms
| Japanese | English | Note |
|---|---|---|
| イデア | Idea | currency/EXP; never "inspiration" |
| イデアの小さな集合体 / 集合体 / 大きな集合体 / 凝縮体 | Small Cluster of Idea / Cluster of Idea / Large Cluster of Idea / Condensed Idea | |
| カルマ | Karma | |
| 紋章指輪 | Crest Ring | `復讐の紋章指輪` → Vengeful Crest Ring |
| 紋章紙片 | Talisman | `炎の紋章紙片` → Flame Talisman; `憶の紋章紙片` → Homeward Talisman |
| 〜紙片・即詠 | Quick-Cast ... Talisman | spell versions of talismans, e.g. `聖紙片・即詠` → Quick-Cast Sacred Talisman |
| 潜在石 | Potentite | `潜在石の欠片` → Potentite Shard; `輝く潜在石の塊` → Shining Potentite Chunk |
| 錠前開けの針金 | Lockpick | |
| 魔女術 / 術 | Witchcraft / spell | |
| 復讐の一撃 | Revenge Strike | |
| パリィ | Parry | |
| 異端審問官 | Inquisitor | `上級異端審問官` → High Inquisitor |
| 聖職者 | cleric | |
| 教会 | Church | |
| 奴隷 | slave | |
| 陵辱された数 | Times Defeated (Violated) | status screen counter; keep short |
| 決定 | Confirm | button name |
| 拾う | Pick up | |
| 習得 | Learn | |

## Terms fixed by the item-data batch (binding for all later batches)
| Japanese | English |
|---|---|
| デグレイド | Degraid (bandit leader) |
| ヴェンデッタ / ヴィオレッタ | Vendetta / Violetta |
| 八ツ手 / 八つ手の悪魔 | Yatsude / Yatsude the Demon (boss title) |
| 無名湖 | Nameless Lake |
| 地下教会区ロデニア / 旧地下墓地街 | Rodenia Underground Parish / Old Catacomb Town |
| 罪人奴隷 / 囚人 / 浮浪者 / 巡礼者 / 看守 | Convict Slave / Prisoner / Vagrant / Pilgrim / Jailer |
| 異端審問官補佐 / 下級異端審問官 | Inquisitor's Aide / Junior Inquisitor |
| 武器物語 / 必要能力値 / 強靭削り値 | Weapon Lore / Required Stats / Poise Damage |
| voices | Vendetta: archaic ("ye/thee"); goblins: broken English; vagrants/prisoners: rough dialect |
| 祈りの乙女 / 絶望の乙女 | Prayer Maiden / Despairing Maiden |
| 堕ちた鎮魂騎士 | Fallen Requiem Knight |
| 教会の獣 | Church Beast |
| 悪魔 (繭背負いの悪魔 / 情欲の悪魔 / 牛頭の悪魔) | Demon (Cocoon-Bearing Demon / Demon of Lust / Bull-Headed Demon) |
| 大鬼 / 小鬼 | ogre / goblin |
| 白の大聖堂 | White Cathedral |
| 空中巡礼道 | Aerial Pilgrim's Path |
| 研究施設 | research facility |
| 祭壇 | altar |
| 本質 | essence |
| 魔女の秘薬 / 魔女の魔力薬 | Witch's Elixir / Witch's Ether |
| 高位なるイデアの集合体 | Exalted Cluster of Idea |
| 潜在石の凝縮体 / 輝く潜在石の凝縮体 | Condensed Potentite / Shining Condensed Potentite |
| 憶亡の紋章布 | Forsaken Homeward Cloth |
| 異質 (upgrade path) | Anomalous |
| weapon prefixes 堕ちた / 叡智なる / 知なる / 堅牢な / 激質な / 巧妙な / 幸運な / 迅速な / 欲深な / 重厚な / 復讐の / 大狂い | Fallen / Sagacious / Learned / Sturdy / Fierce / Deft / Lucky / Swift / Greedy / Heavy / Vengeful / Raving Mad |
| 終代 鈴音 | Suzune the Last |
| 行動力 | Stamina (SP) |
| リベンジ | Revenge |
| 未装備 | Unequipped |
| 憶紙片・即詠 | Quick-Cast Homeward Talisman |
| 異端審問官の散弾銃 | Inquisitor's Blunderbuss |
| 魔女の銃 | Witch's Pistol |

## Terms fixed by the UI/code batch
| Japanese | English |
|---|---|
| 陵辱された数 | Times Violated (replaces the earlier "Times Defeated (Violated)") |
| 決定ボタン | Confirm button |
| 攻撃 / 救出 / 調べる / 使用 / 解放する (prompts) | Attack / Rescue / Examine / Use / Release |
| 中級異端審問官 / 上級異端審問官 | Senior Inquisitor / High Inquisitor |
| 屍者 | Undead |
| 修道女騎士 / 鎮魂の騎士 | Nun Knight / Requiem Knight |
| 盗賊 / 盗賊頭 | Bandit / Bandit Chief |
| 黒服 / 調教師 | Man in Black / Trainer |
| 乙女 / 緑の乙女 | Maiden / Green Maiden |
| 繭背負い | Cocoon Bearer |
| 地下聖教会 / 教区教会 / 捨てられた下層 / 盗賊の隠れ森 / 牧場 | Underground Holy Church / Parish Church / Abandoned Underlevel / Bandits' Hidden Forest / Ranch |
| 幻衣装 / 魔女の衣装 / 魔女の剣 | Phantom Garb / Witch's Attire / Witch's Sword |
| 消耗品 / 装備品 | Consumables / Equipment |
| 怯み | stagger |
| 苗床 | brood |
| クラレス / イルゥシブ / カンドーレ / ロデニア / ラグドゥム | Clares / Illusive / Candore / Rodenia / Ragdum |

## Terms fixed by the scene batches
| Japanese | English |
|---|---|
| 悪魔堕ち | demon-fallen (noun and adjective, same form singular/plural) |
| 警備隊 / 警備隊員 / 警備隊長 / 詰所 | city guard / guardsman / Guard Captain / guard post |
| レイナ / クレア / シエラ / 教皇 | Reina / Claire (Aradia's sister) / Sierra / the Pope |
| 鬼神丸 / 喜人丸 | Kijinmaru (pun on the two readings cannot be kept; see review notes) |
| 姉さま / 父さん / 兄ぃ, 兄弟子 | Sister / Father / Brother (sword-school senior) |
| 棄村 | Abandoned Village |
| 修道騎士 / 修道女騎士 | nun knight (lowercase in dialogue) |
| 白装束の女 / 魔女と名乗る女性 | Woman in White / Self-Proclaimed Witch |
| 呪符 | charm (distinct from Talisman) |
| censored words (ペ〇ス, ま〇こ, チ〇ポ) | keep the censoring: p*nis, p*ssy, d*ck |
| 先輩 / 後輩 (inquisitors) | sir / Junior Inquisitor |
| 不夜街 | Nightless Town |
| 鎮魂の巡礼道, 鎮魂の道 / 鎮魂の森 | Path of Requiem / Forest of Requiem |
| 酒場のマスター / 鍛冶屋 / 老婆 | Tavern Master / Blacksmith / Old Woman |
| 快感マヒ | Pleasure Stun |
| 詠唱解除 / 祭壇移動 / 記録保存 / 祭壇に捧げる | Cast Cancel / Altar Travel / Save Record / Offer to Altar |
| 居合, 居合術 | iaijutsu (quick-draw) |
| 幻服 | phantom garment |
| names | Albert, Karen, Snow, Ain, Ilia, Anasta, Hinagiku |
| honorific -さん (polite) | "Miss X" |

Open for human review: Degraid vs "Degrade", 激質な "Fierce", 無銘 湾れ刃 "Unsigned Notare Blade", 終代 "the Last".

## Stats and elements
| Japanese | English |
|---|---|
| 筋力 / 技量 / 知力 / 運 | STR / DEX / INT / LUCK (full words in descriptions: Strength, Dexterity, Intelligence, Luck) |
| 斬 / 突 / 打 | Slash / Thrust / Strike |
| 炎 / 雷 / 聖 / 闇 | Fire / Lightning / Holy / Dark |
| 体力 / 魔力 / スタミナ | HP / MP / Stamina |
