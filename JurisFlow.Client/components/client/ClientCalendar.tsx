import React, { useState, useEffect } from 'react';
import { CalendarEvent } from '../../types';
import { Clock } from '../Icons';
import { clientApi } from '../../services/clientApi';

const ClientCalendar: React.FC = () => {
  const [events, setEvents] = useState<CalendarEvent[]>([]);
  const [matters, setMatters] = useState<any[]>([]);
  const [loading, setLoading] = useState(true);
  const [currentMonth, setCurrentMonth] = useState(new Date().getMonth());
  const [currentYear, setCurrentYear] = useState(new Date().getFullYear());

  useEffect(() => {
    const loadData = async () => {
      try {
        const mattersData = await clientApi.fetchJson('/matters');
        setMatters(mattersData);
        
        // Get events from matters - flatten all events from all matters
        const allEvents: CalendarEvent[] = [];
        mattersData.forEach((matter: any) => {
          if (matter.events && Array.isArray(matter.events)) {
            matter.events.forEach((event: any) => {
              // Ensure date is in ISO string format
              const eventDate = event.date instanceof Date 
                ? event.date.toISOString() 
                : typeof event.date === 'string' 
                  ? event.date 
                  : new Date(event.date).toISOString();
              
              allEvents.push({
                id: event.id,
                title: event.title,
                date: eventDate,
                type: event.type || 'Meeting',
                matterId: matter.id
              });
            });
          }
        });
        // Sort events by date
        allEvents.sort((a, b) => new Date(a.date).getTime() - new Date(b.date).getTime());
        setEvents(allEvents);
      } catch (error) {
        console.error('Error loading calendar:', error);
      } finally {
        setLoading(false);
      }
    };
    
    loadData();
  }, []);

  const getDaysInMonth = (year: number, month: number) => new Date(year, month + 1, 0).getDate();
  const getFirstDayOfMonth = (year: number, month: number) => new Date(year, month, 1).getDay();

  const daysInMonth = getDaysInMonth(currentYear, currentMonth);
  const startDay = getFirstDayOfMonth(currentYear, currentMonth);
  const emptySlots = Array.from({ length: startDay });
  const daySlots = Array.from({ length: daysInMonth }, (_, i) => i + 1);

  const currentDate = new Date();

  if (loading) {
    return (
      <div className="flex items-center justify-center h-full">
        <div className="text-gray-400">Loading...</div>
      </div>
    );
  }

  return (
    <div className="p-8 h-full overflow-y-auto">
      <div className="mb-6 flex justify-between items-center">
        <div>
          <h2 className="text-2xl font-bold text-slate-900">Calendar</h2>
          <p className="text-gray-600 mt-1">
            {new Date(currentYear, currentMonth).toLocaleDateString('en-US', { month: 'long', year: 'numeric' })}
          </p>
        </div>
        <div className="flex gap-2">
          <button 
            onClick={() => {
              if (currentMonth === 0) { setCurrentMonth(11); setCurrentYear(y => y - 1); }
              else { setCurrentMonth(m => m - 1); }
            }} 
            className="p-2 hover:bg-gray-200 rounded text-slate-600"
          >
            &larr;
          </button>
          <button 
            onClick={() => {
              if (currentMonth === 11) { setCurrentMonth(0); setCurrentYear(y => y + 1); }
              else { setCurrentMonth(m => m + 1); }
            }} 
            className="p-2 hover:bg-gray-200 rounded text-slate-600"
          >
            &rarr;
          </button>
        </div>
      </div>

      <div className="flex gap-6">
        {/* Calendar Grid */}
        <div className="flex-1 bg-white rounded-xl shadow-sm border border-gray-200 flex flex-col overflow-hidden">
          <div className="grid grid-cols-7 border-b border-gray-200 bg-gray-50">
            {['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat'].map(d => (
              <div key={d} className="py-3 text-center text-xs font-bold text-gray-500 uppercase tracking-wide">
                {d}
              </div>
            ))}
          </div>
          
          <div className="flex-1 grid grid-cols-7 grid-rows-5 divide-x divide-y divide-gray-100">
            {emptySlots.map((_, i) => <div key={`empty-${i}`} className="bg-gray-50/30"></div>)}
            
            {daySlots.map((day) => {
              const isToday = 
                day === currentDate.getDate() && 
                currentMonth === currentDate.getMonth() && 
                currentYear === currentDate.getFullYear();
              
              const dayEvents = events.filter(e => {
                const d = new Date(e.date);
                return d.getDate() === day && d.getMonth() === currentMonth && d.getFullYear() === currentYear;
              });

              return (
                <div 
                  key={day} 
                  className={`min-h-[100px] p-2 relative flex flex-col gap-1 ${isToday ? 'bg-blue-50/50' : ''}`}
                >
                  <span className={`text-sm font-bold w-7 h-7 flex items-center justify-center rounded-full ${isToday ? 'bg-blue-600 text-white shadow-md' : 'text-gray-700'}`}>
                    {day}
                  </span>
                  
                  <div className="flex flex-col gap-1 mt-1">
                    {dayEvents.map(ev => (
                      <div 
                        key={ev.id} 
                        className={`text-[10px] px-1.5 py-0.5 rounded truncate font-medium ${
                          ev.type === 'Court' ? 'bg-red-100 text-red-700' :
                          ev.type === 'Deadline' ? 'bg-amber-100 text-amber-700' :
                          'bg-blue-100 text-blue-700'
                        }`}
                        title={ev.title}
                      >
                        {ev.title}
                      </div>
                    ))}
                  </div>
                </div>
              );
            })}
          </div>
        </div>

        {/* Upcoming Events Sidebar */}
        <div className="w-80 bg-white rounded-xl shadow-sm border border-gray-200 p-6 flex flex-col">
          <h3 className="font-bold text-lg text-slate-900 mb-4">Upcoming Events</h3>
          {events.length === 0 ? (
            <div className="flex-1 flex flex-col items-center justify-center text-gray-400 text-sm italic">
              No upcoming events.
            </div>
          ) : (
            <div className="space-y-4 overflow-y-auto pr-2">
              {events
                .filter(e => new Date(e.date) >= new Date())
                .sort((a, b) => new Date(a.date).getTime() - new Date(b.date).getTime())
                .slice(0, 10)
                .map(event => {
                  const matter = matters.find(m => m.id === event.matterId);
                  return (
                    <div key={event.id} className="flex gap-3 relative group">
                      <div className="flex flex-col items-center">
                        <div className="w-2 h-2 rounded-full bg-slate-400 mt-2"></div>
                        <div className="w-px h-full bg-gray-200 my-1"></div>
                      </div>
                      <div className="pb-4">
                        <p className="text-sm font-semibold text-gray-800">{event.title}</p>
                        {matter && (
                          <p className="text-xs text-blue-600 mt-1">Case: {matter.caseNumber}</p>
                        )}
                        <p className="text-xs text-gray-500 flex items-center gap-1 mt-1">
                          <Clock className="w-3 h-3" />
                          {new Date(event.date).toLocaleDateString() + " " + new Date(event.date).toLocaleTimeString([], {hour: '2-digit', minute:'2-digit'})}
                        </p>
                        <span className={`inline-block mt-2 px-2 py-0.5 rounded text-[10px] uppercase font-bold tracking-wide
                          ${event.type === 'Court' ? 'bg-red-50 text-red-700' : 
                            event.type === 'Deadline' ? 'bg-amber-50 text-amber-700' : 'bg-blue-50 text-blue-700'
                          }`}>
                          {event.type}
                        </span>
                      </div>
                    </div>
                  );
                })}
            </div>
          )}
        </div>
      </div>
    </div>
  );
};

export default ClientCalendar;

