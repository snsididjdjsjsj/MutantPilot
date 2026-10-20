"""Read the harness's five-second frame windows without controlling the game."""
import argparse
from datetime import datetime, timezone
import json
from pathlib import Path
import time

parser=argparse.ArgumentParser(description=__doc__)
parser.add_argument('--output',type=Path,required=True)
parser.add_argument('--seconds',type=float,default=900)
args=parser.parse_args()
status=Path.home()/'Documents/My Games/Terraria/tModLoader/MutantPilot/status.json'
deadline=time.monotonic()+args.seconds
previous=None
with args.output.open('a',encoding='utf8') as destination:
    while time.monotonic()<deadline:
        try:
            current=json.loads(status.read_text(encoding='utf8'))
            age=(datetime.now(timezone.utc)-datetime.fromisoformat(current['utc'].replace('Z','+00:00'))).total_seconds()
            window=current.get('performance')
            if age<3 and window and current['boss']:
                key=json.dumps(window,sort_keys=True)
                if key!=previous:
                    previous=key
                    row={'utc':current['utc'],'version':current['version'],'tick':current['player']['tick'],
                         'state':current['boss']['state'],'hits':current['player']['hits'],**window}
                    destination.write(json.dumps(row)+'\n');destination.flush()
            elif age<3 and current['boss'] is None and previous:
                break
        except (OSError,ValueError,KeyError):
            pass
        time.sleep(1)
