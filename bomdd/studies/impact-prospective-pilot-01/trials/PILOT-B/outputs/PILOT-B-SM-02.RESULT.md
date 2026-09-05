prediction:
  request: REQUEST-R1
  change_units:
    - unit: M-DB-007
      confidence: likely
      rationale: "bom/32-mbom.yaml M-DB-007 interface_contract.repositories はコレクション切替用の GetNormalByFolderAsync・GetImageTagsByFolderAsync・CountByFolderAndStatusAsync とスキーマを所有する。REQUEST-R1 の遅延が hidden missing 件数に相関するため、対象限定クエリまたは索引の是正先として最有力。"
  investigate_units:
    - unit: M-UI-IMAGETAB-035
      confidence: certain
      rationale: "bom/32-mbom.yaml M-UI-IMAGETAB-035 interface_contract.startup はコレクション切替、当該 normal+image_tags+deleted count のロード、generation/CTS を所有する。過剰な全状態取得や不要な再構築の有無を最初に確認する必要がある。"
    - unit: M-UI-013
      confidence: uncertain
      rationale: "bom/32-mbom.yaml M-UI-013 interface_contract.startup は MainWindow.InitializeAsync と ImageTab の catalog loading 接続を所有する。ただし通常のコレクション切替がこのシェル経路を通るかは BOM だけでは確定しない。"
  not_affected:
    - unit: M-SIMSEARCH-021
      rationale: "bom/32-mbom.yaml M-SIMSEARCH-021 は FindSimilarAsync/FindSimilarInScopeAsync による類似画像検索を所有する。REQUEST-R1 は名称が「類似」のコレクションへの切替であり、類似検索実行の要求ではない。"
    - unit: M-RELINK-025
      rationale: "bom/32-mbom.yaml M-RELINK-025 は missing 画像の候補選別・再リンク確定を所有する。missing 262,045件は性能相関の手掛かりだが、通常の画像ブラウズ切替では修復ロードを行わない。"
  ebom_items:
    - E-DB-010
    - E-UI-BROWSE-022
    - E-UI-SHELL-021
  contracts_to_check:
    - CP-STARTUP-028
    - CP-NFR-026
    - CP-UI-G1
    - CP-DB-006
  unresolved:
    - question: "遅延を支配する処理は normal画像取得、image_tags取得、deleted件数取得、または ViewModel の一覧再構築のどれか。"
      needs: "各 repository 呼出しと一覧再構築の区間計測、および REQUEST-R1 相当データでの呼出し回数。"
    - question: "M-DB-007 の対象限定SQLが missing 行を走査する実行計画か、M-UI-IMAGETAB-035 が全状態を取得して後段フィルタしているか。"
      needs: "実際の repository 呼出し引数・SQL・SQLite query plan。BOM には具体的SQLと対象索引が記載されていない。"
    - question: "是正後に合格とする切替時間または計算量上限。"
      needs: "REQUEST-R1 は相対的期待のみで実測値を持たず、CP-STARTUP-028 と CP-NFR-026 も固定時間閾値を置いていないため、受入用の決定論的上限または比較測定条件。"
