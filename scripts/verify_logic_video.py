"""Verify that an encoded video has one contiguous native game tick per 60 FPS frame."""
import argparse
import json
from pathlib import Path
import subprocess

parser=argparse.ArgumentParser(description=__doc__)
parser.add_argument('video',type=Path)
parser.add_argument('--ffprobe',type=Path,required=True)
parser.add_argument('--output',type=Path)
parser.add_argument('--hardware-decode',action='store_true',help='Decode every frame with NVDEC, and cross-check MP4 packet/frame counts')
args=parser.parse_args()
capture=json.loads(Path(str(args.video)+'.capture.json').read_text())
rows=[json.loads(line) for line in Path(str(args.video)+'.frames.jsonl').read_text().splitlines()]
assert rows and capture['error'] is None and capture['outcome']['error'] is None
assert capture['outcome']['missingGameFrames']==0
assert all(row['frame']==i for i,row in enumerate(rows))
assert all((b['gameTick']-a['gameTick'])%(2**32)==1 for a,b in zip(rows,rows[1:]))
assert len(rows)==capture['frames']==capture['outcome']['frames']
assert rows[0]['gameTick']==capture['firstGameTick'] and rows[-1]['gameTick']==capture['lastGameTick']
command=[str(args.ffprobe),'-v','error','-count_packets' if args.hardware_decode else '-count_frames','-select_streams','v:0','-show_entries',
         'stream=width,height,r_frame_rate,avg_frame_rate,nb_frames,nb_read_frames,nb_read_packets,duration','-of','json',str(args.video)]
stream=json.loads(subprocess.check_output(command,encoding='utf8'))['streams'][0]
assert stream['r_frame_rate']=='60/1' and stream['avg_frame_rate']=='60/1'
if args.hardware_decode:
    assert int(stream['nb_read_packets'])==int(stream['nb_frames'])==len(rows)
    ffmpeg=args.ffprobe.with_name('ffmpeg'+args.ffprobe.suffix)
    command=[str(ffmpeg),'-hide_banner','-loglevel','error','-nostats','-progress','pipe:1',
             '-hwaccel','cuda','-hwaccel_output_format','cuda','-i',str(args.video),'-map','0:v:0',
             '-an','-sn','-dn','-fps_mode','passthrough','-f','null','-']
    progress=subprocess.check_output(command,encoding='utf8',stderr=subprocess.PIPE)
    decoded=[int(line.split('=',1)[1]) for line in progress.splitlines() if line.startswith('frame=')]
    assert decoded and decoded[-1]==len(rows) and 'progress=end' in progress
    stream['nb_decoded_frames']=decoded[-1]
else:
    assert int(stream['nb_read_frames'])==len(rows)
assert abs(float(stream['duration'])-len(rows)/60)<.0001
report={'verified_contiguous_native_60fps':True,'video':str(args.video.resolve()),'frames':len(rows),
        'video_seconds':len(rows)/60,'wall_seconds':capture['wallSeconds'],'audio':capture['audio'],
        'source_width':capture['width'],'source_height':capture['height'],'encoded':stream,
        'decode_method':'NVDEC all frames' if args.hardware_decode else 'ffprobe all frames','outcome':capture['outcome']}
if args.output:args.output.write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf8')
print(json.dumps(report,ensure_ascii=False))
