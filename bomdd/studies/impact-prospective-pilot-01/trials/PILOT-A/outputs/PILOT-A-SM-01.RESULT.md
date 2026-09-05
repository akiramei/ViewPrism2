prediction:
  request: REQUEST-R1
  change_units:
    - unit: M-UI-TRASH-032
      confidence: certain
      rationale: "bom/32-mbom.yaml M-UI-TRASH-032 acceptance_note は、ECO-098以降の一覧ロードを GetDeletedByFolderAsync のみに限定し、全 status 取得への退行を禁止する。bom/30-ebom.yaml E-UI-REPAIR-039 ECO-098 は、normal 6件・hidden missing 262,045件という本要求と一致する実profileで、後段filter前の全件materializeが約1.1秒停止を生んだと記録する。"
    - unit: M-DB-007
      confidence: certain
      rationale: "bom/32-mbom.yaml M-DB-007 interface_contract.repositories は、ECO-098用の選択collection+deleted限定read model GetDeletedByFolderAsync を本unitの所有契約として明記する。"
  investigate_units:
    - unit: M-UI-IMAGETAB-035
      confidence: likely
      rationale: "bom/32-mbom.yaml M-UI-IMAGETAB-035 はcollection切替とcontent reloadを所有し、M-UI-TRASH-032を子VMとして生成する。切替時に不要なゴミ箱ロードまたは全面Recomputeが結合していないか確認が必要だが、ECO-098の直接是正先とはBOM上で断定できない。"
  not_affected:
    - unit: M-SIMSEARCH-021
      rationale: "REQUEST-R1.md の「類似」はコレクション名であり検索実行の報告ではない。bom/32-mbom.yaml M-SIMSEARCH-021/FMEA-022 は、整理モードで類似検索を実行した際の候補scope・pHash処理を対象とする。"
    - unit: M-SCAN-005
      rationale: "REQUEST-R1.md は既存コレクションの表示切替を対象とし、スキャン実行中とは記載しない。M-SCAN-005の責務は列挙・ハッシュ・status遷移・段階公開である。"
  ebom_items:
    - E-UI-REPAIR-039
    - E-UI-MODE-041
    - E-DB-010
    - E-UI-SHELL-021
  contracts_to_check:
    - CP-UI-G1
    - CP-STARTUP-028
    - CP-DB-006
    - CP-L1-SMOKE
  unresolved:
    - question: "切替時の遅延が、ゴミ箱用の全status取得そのものか、親M-UI-IMAGETAB-035からの不要なreload/Recompute結合も含むか。"
      needs: "選択切替1回について、GetByFolderAsync・GetDeletedByFolderAsync・GetNormalByFolderAsync・GetImageTagsByFolderAsyncの呼出回数と引数、および各処理区間の計測。"
    - question: "専用deleted queryへの置換だけで十分か、DB索引の追加も必要か。"
      needs: "262,045件のmissingを含むfixtureでのquery planと、専用query・content reload各区間の実測。"