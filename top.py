#!/usr/bin/env python3
import os
import sys
import uuid
import pandas as pd

from perfetto.trace_builder.proto_builder import TraceProtoBuilder
from perfetto.protos.perfetto.trace.perfetto_trace_pb2 import TrackEvent, TrackDescriptor, ProcessDescriptor, ThreadDescriptor

TRUSTED_PACKET_SEQUENCE_ID = 0x1234 # 选择任何唯一的整数
builder = TraceProtoBuilder()

def define_track(group_name, parent_track_uuid=None):
    track_uuid = uuid.uuid4().int & ((1 << 63) - 1)
    packet = builder.add_packet()
    packet.track_descriptor.uuid = track_uuid
    packet.track_descriptor.name = group_name
    if parent_track_uuid:
        packet.track_descriptor.parent_uuid = parent_track_uuid
    return track_uuid
    
def add_slice_event(ts, event_type, event_track_uuid, name=None):
    packet = builder.add_packet()
    packet.timestamp = ts
    packet.track_event.type = event_type
    packet.track_event.track_uuid = event_track_uuid
    if name:
        packet.track_event.name = name
    packet.trusted_packet_sequence_id = TRUSTED_PACKET_SEQUENCE_ID
    return packet

#def populate_packets(builder: TraceProtoBuilder):
def populate_packets(input_name):
    
    process = {}
    for chunkReader in pd.read_csv(input_name, chunksize=10000):
        for _, row in chunkReader.iterrows():
            name = row["ProcessName"]
            pid = row["NewPid"]
            tid = row["NewTid"]
            proc = process.get(name)
            if proc is None:
                process[name] = {}
                proc = process.get(name)
                proc["trackId"] = define_track(f"{name} ({pid})")
            thr = proc.get(tid)
            if thr is None:
                proc[tid] = {}
                thr = proc.get(tid)
                thr["trackId"] = define_track(f"{tid}", proc["trackId"])
            stackNew = str(row["Tags"]).split('/')
            indexNew = -1
            stackOld = thr.get("stacks")
            indexOld = thr.get("index")
            timeStop = thr.get("stop")
            packet = None
            if stackOld is None:
                for stk in stackNew:
                    packet = add_slice_event(row["TimeStamp"], TrackEvent.TYPE_SLICE_BEGIN, thr["trackId"], stk)
            else:
                for i in range(min(len(stackNew), len(stackOld))):
                    if stackNew[i] != stackOld[i]:
                        break
                    indexNew = i
                if indexNew == -1:
                    for stk in stackOld:
                        add_slice_event(timeStop, TrackEvent.TYPE_SLICE_END, thr["trackId"])
                    for stk in stackNew:
                        packet = add_slice_event(row["TimeStamp"], TrackEvent.TYPE_SLICE_BEGIN, thr["trackId"], stk)
                elif indexNew == indexOld:
                    for idx, stk in enumerate(stackOld):
                        if idx > indexOld:
                            add_slice_event(timeStop, TrackEvent.TYPE_SLICE_END, thr["trackId"])
                    for idx, stk in enumerate(stackNew):
                        if idx > indexNew:
                            packet = add_slice_event(row["TimeStamp"], TrackEvent.TYPE_SLICE_BEGIN, thr["trackId"], stk)
                else:
                    for idx, stk in enumerate(stackOld):
                        if idx > indexNew:
                            add_slice_event(timeStop, TrackEvent.TYPE_SLICE_END, thr["trackId"])
                    for idx, stk in enumerate(stackNew):
                        if idx > indexNew:
                            packet = add_slice_event(row["TimeStamp"], TrackEvent.TYPE_SLICE_BEGIN, thr["trackId"], stk)
            if packet != None:
                annotation = packet.track_event.debug_annotations.add()
                annotation.name = "ready_process"
                annotation.string_value = f"{row['OldName']} {row['OldTid']}"
            thr["stacks"] = stackNew
            thr["index"] = indexNew
            thr["stop"] = row["TimeStop"]
    for pid in process:
        threads = process.get(pid)
        for tid in threads:
            if tid == "trackId":
                continue
            thr = threads.get(tid)
            stacks = thr.get("stacks")
            index = thr.get("index")
            timeStop = thr.get("stop")
            if stacks is not None:
                #print(f"close {pid} {tid} {index} {timeStop}")
                for idx, stk in enumerate(stacks):
                    add_slice_event(timeStop, TrackEvent.TYPE_SLICE_END, thr["trackId"])

def main():
    input_name = ""
    sort_need = False
    for a in sys.argv:
        if a == "-s":
            sort_need = True
        else:
            input_name = a
    if sort_need == True:
        pd.read_csv(input_name).sort_values('TimeStamp').to_csv(input_name, index=False)
    dir_name = os.path.dirname(input_name)
    base_name = os.path.basename(input_name)
    output_name = os.path.splitext(base_name)[0]+".pftrace"
    output_name = os.path.join(dir_name, output_name)
    
    populate_packets(input_name)

    with open(output_name, 'wb') as f:
      f.write(builder.serialize())

if __name__ == "__main__":
    main()