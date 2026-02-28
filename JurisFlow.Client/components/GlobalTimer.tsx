import React, { useState, useEffect, useRef } from 'react';
import { Timer, Pause, X } from './Icons';
import { useData } from '../contexts/DataContext';
import { useTranslation } from '../contexts/LanguageContext';
import { toast } from './Toast';
import { ActivityCode } from '../types';

// Simple standalone component for the "Wow" factor
const GlobalTimer: React.FC = () => {
  const { addTimeEntry, matters } = useData();
  const { t } = useTranslation();
  const [isOpen, setIsOpen] = useState(false);
  const [isRunning, setIsRunning] = useState(false);
  const [seconds, setSeconds] = useState(0);
  const [description, setDescription] = useState('');
  const [selectedMatterId, setSelectedMatterId] = useState('');
  const intervalRef = useRef<any>(null);

  const formatTime = (totalSeconds: number) => {
    const h = Math.floor(totalSeconds / 3600).toString().padStart(2, '0');
    const m = Math.floor((totalSeconds % 3600) / 60).toString().padStart(2, '0');
    const s = (totalSeconds % 60).toString().padStart(2, '0');
    return `${h}:${m}:${s}`;
  };

  useEffect(() => {
    if (isRunning) {
      intervalRef.current = setInterval(() => {
        setSeconds(s => s + 1);
      }, 1000);
    } else {
      clearInterval(intervalRef.current);
    }
    return () => clearInterval(intervalRef.current);
  }, [isRunning]);

  useEffect(() => {
    if (matters.length > 0 && selectedMatterId && !matters.some(matter => matter.id === selectedMatterId)) {
      setSelectedMatterId('');
      toast.warning('Selected matter is no longer available. Please reselect.');
    }
  }, [matters, selectedMatterId]);

  // Mini State (Collapsed) vs Open State
  if (!isOpen) {
    return (
      <button 
        onClick={() => setIsOpen(true)}
        className={`flex items-center gap-2 px-3 py-1.5 rounded-lg transition-all duration-300 hover:scale-105 ${isRunning ? 'bg-amber-500 text-white animate-pulse' : 'bg-slate-800 text-white'}`}
      >
         {isRunning ? <Pause className="w-4 h-4"/> : <Timer className="w-4 h-4" />}
         <span className="font-mono font-bold text-sm tracking-wider">{formatTime(seconds)}</span>
      </button>
    );
  }

  return (
    <div className="absolute top-full right-0 mt-2 z-50 bg-white rounded-2xl shadow-2xl border border-gray-200 w-80 overflow-hidden animate-in slide-in-from-top-5 fade-in duration-300">
       <div className="bg-slate-900 px-4 py-3 flex justify-between items-center text-white">
          <div className="flex items-center gap-2">
             <div className={`w-2 h-2 rounded-full ${isRunning ? 'bg-amber-500 animate-pulse' : 'bg-gray-500'}`}></div>
             <span className="text-sm font-bold">Global Timer</span>
          </div>
          <button onClick={() => setIsOpen(false)} className="text-gray-400 hover:text-white"><X className="w-4 h-4"/></button>
       </div>
       
       <div className="p-6 flex flex-col items-center">
          <div className="text-5xl font-mono font-bold text-slate-800 tracking-wider mb-2">
              {formatTime(seconds)}
          </div>
          <p className="text-xs text-gray-400 uppercase tracking-widest mb-6">Billable Time</p>
          
          <div className="w-full space-y-3">
              <input 
                type="text" 
                placeholder="What are you working on?" 
                value={description}
                onChange={(e) => setDescription(e.target.value)}
                className="w-full bg-gray-50 border border-gray-200 rounded-lg px-3 py-2 text-sm outline-none focus:ring-2 focus:ring-slate-500" 
              />
              
              <select
                value={selectedMatterId}
                onChange={(e) => setSelectedMatterId(e.target.value)}
                className="w-full bg-gray-50 border border-gray-200 rounded-lg px-3 py-2 text-sm outline-none focus:ring-2 focus:ring-slate-500"
              >
                <option value="">Free/Unassigned</option>
                {matters.map(matter => (
                  <option key={matter.id} value={matter.id}>
                    {matter.caseNumber} - {matter.name}
                  </option>
                ))}
              </select>
              
              <div className="flex gap-2">
                  <button 
                    onClick={() => setIsRunning(!isRunning)}
                    className={`flex-1 py-2.5 rounded-lg font-bold text-sm text-white transition-colors ${isRunning ? 'bg-amber-500 hover:bg-amber-600' : 'bg-green-600 hover:bg-green-700'}`}
                  >
                      {isRunning ? 'Pause' : 'Start'}
                  </button>
                  <button 
                    onClick={async () => { 
                      if (seconds > 0) {
                        const saved = await addTimeEntry({
                          id: `t${Date.now()}`,
                          matterId: selectedMatterId || undefined,
                          description: description || 'General Legal Services',
                          duration: Math.ceil(seconds / 60),
                          rate: 450,
                          date: new Date().toISOString(),
                          billed: false,
                          type: 'time',
                          activityCode: ActivityCode.A103,
                          isBillable: true
                        });
                        if (saved) {
                          setIsRunning(false);
                          setSeconds(0);
                          setDescription('');
                          setSelectedMatterId('');
                          setIsOpen(false);
                        } else {
                          toast.error('Failed to log time entry');
                        }
                      } else {
                        setIsRunning(false);
                        setSeconds(0);
                        setIsOpen(false);
                      }
                    }}
                    className="px-4 py-2.5 rounded-lg font-bold text-sm bg-gray-100 text-gray-600 hover:bg-gray-200"
                  >
                      Log
                  </button>
              </div>
          </div>
       </div>
    </div>
  );
};

export default GlobalTimer;
