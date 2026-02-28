import React, { useState, useEffect } from 'react';
import { CalendarEvent, AppointmentRequest, Employee } from '../types';
import { Clock, Plus, X, ChevronRight, Trash } from './Icons';
import { useTranslation } from '../contexts/LanguageContext';
import { translations } from '../translations';
import { useData } from '../contexts/DataContext';
import { useConfirm } from './ConfirmDialog';
import { api } from '../services/api';
import { toast } from './Toast';

const CalendarView: React.FC = () => {
  const { t, language } = useTranslation();
  const { events, addEvent, updateEvent, deleteEvent, tasks, matters } = useData();
  const { confirm } = useConfirm();
  const daysShort = (translations[language] as any)?.days_short || translations['en'].days_short;

  const [showModal, setShowModal] = useState(false);
  const [selectedDate, setSelectedDate] = useState<Date | null>(new Date());

  // Form State
  const [newTitle, setNewTitle] = useState('');
  const [newType, setNewType] = useState('Meeting');
  const [newTime, setNewTime] = useState('09:00');
  const [newDuration, setNewDuration] = useState(60); // dakika
  const [newReminderMinutes, setNewReminderMinutes] = useState(30); // default 30 minutes
  const [newDescription, setNewDescription] = useState('');
  const [newLocation, setNewLocation] = useState('');
  const [newRecurrence, setNewRecurrence] = useState('none');

  type AppointmentItem = AppointmentRequest & {
    client?: { id: string; name: string; email?: string };
  };

  const [appointments, setAppointments] = useState<AppointmentItem[]>([]);
  const [appointmentsLoading, setAppointmentsLoading] = useState(false);
  const [appointmentFilter, setAppointmentFilter] = useState<'pending' | 'approved' | 'rejected' | 'cancelled' | 'all'>('pending');
  const [showAppointmentModal, setShowAppointmentModal] = useState(false);
  const [activeAppointment, setActiveAppointment] = useState<AppointmentItem | null>(null);
  const [scheduleDate, setScheduleDate] = useState('');
  const [scheduleTime, setScheduleTime] = useState('09:00');
  const [scheduleDuration, setScheduleDuration] = useState(30);
  const [assignedTo, setAssignedTo] = useState('');
  const [employees, setEmployees] = useState<Employee[]>([]);

  // --- CALENDAR LOGIC ---
  const currentDate = new Date();
  const [currentMonth, setCurrentMonth] = useState(currentDate.getMonth());
  const [currentYear, setCurrentYear] = useState(currentDate.getFullYear());

  const getDaysInMonth = (year: number, month: number) => new Date(year, month + 1, 0).getDate();
  const getFirstDayOfMonth = (year: number, month: number) => new Date(year, month, 1).getDay();

  const daysInMonth = getDaysInMonth(currentYear, currentMonth);
  const startDay = getFirstDayOfMonth(currentYear, currentMonth); // 0 (Sun) to 6 (Sat)

  // Generate calendar grid array
  // Padding for empty cells before the 1st of the month
  const emptySlots = Array.from({ length: startDay });
  const daySlots = Array.from({ length: daysInMonth }, (_, i) => i + 1);

  const handleAddEvent = (e: React.FormEvent) => {
    e.preventDefault();
    if (!newTitle || !selectedDate) return;

    const [hour, minute] = newTime.split(':').map(Number);
    const eventDate = new Date(currentYear, currentMonth, selectedDate.getDate(), hour || 9, minute || 0, 0);

    addEvent({
      id: `ev${Date.now()}`,
      title: newTitle,
      date: eventDate.toISOString(),
      type: newType as any,
      duration: newDuration,
      reminderMinutes: newReminderMinutes,
      reminderSent: false,
      description: newDescription || undefined,
      location: newLocation || undefined,
      recurrencePattern: newRecurrence !== 'none' ? newRecurrence as any : undefined
    });
    // Reset form
    setShowModal(false);
    setNewTitle('');
    setNewDuration(60);
    setNewReminderMinutes(30);
    setNewDescription('');
    setNewLocation('');
    setNewRecurrence('none');
  };

  const openAddModal = (day: number) => {
    setSelectedDate(new Date(currentYear, currentMonth, day));
    setShowModal(true);
  };

  const loadAppointments = async () => {
    setAppointmentsLoading(true);
    try {
      const data = await api.getAppointments();
      setAppointments(Array.isArray(data) ? data : []);
    } catch (error) {
      console.error('Failed to load appointments', error);
      toast.error('Unable to load appointment requests.');
    } finally {
      setAppointmentsLoading(false);
    }
  };

  const loadEmployees = async () => {
    try {
      const data = await api.getEmployees();
      setEmployees(Array.isArray(data) ? data : []);
    } catch (error) {
      console.error('Failed to load employees', error);
    }
  };

  useEffect(() => {
    loadAppointments();
    loadEmployees();
  }, []);

  const openScheduleModal = (appointment: AppointmentItem) => {
    setActiveAppointment(appointment);
    const baseDate = appointment.status === 'approved' && appointment.approvedDate
      ? new Date(appointment.approvedDate)
      : new Date(appointment.requestedDate);
    setScheduleDate(baseDate.toISOString().slice(0, 10));
    setScheduleTime(baseDate.toTimeString().slice(0, 5));
    setScheduleDuration(appointment.duration || 30);
    setAssignedTo(appointment.assignedTo || '');
    setShowAppointmentModal(true);
  };

  const updateAppointmentStatus = async (appointment: AppointmentItem, status: AppointmentItem['status'], approvedDate?: Date, assignedToValue?: string, duration?: number) => {
    try {
      const updated = await api.updateAppointment(appointment.id, {
        status,
        approvedDate: approvedDate ? approvedDate.toISOString() : undefined,
        assignedTo: assignedToValue || undefined,
        duration: duration || undefined
      });
      if (updated) {
        setAppointments(prev => prev.map(item => item.id === appointment.id ? { ...item, ...updated } : item));
      }
      return updated;
    } catch (error) {
      console.error('Failed to update appointment', error);
      toast.error('Failed to update appointment.');
      return null;
    }
  };

  const appointmentTag = (id: string) => `AppointmentId:${id}`;
  const buildAppointmentDescription = (notes: string | undefined, id: string) => {
    const trimmed = notes?.trim();
    const tag = appointmentTag(id);
    return trimmed ? `${trimmed}\n\n${tag}` : tag;
  };
  const findAppointmentEvent = (id: string) => {
    const tag = appointmentTag(id);
    return events.find(e => (e.description || '').includes(tag));
  };

  const handleApproveAppointment = async () => {
    if (!activeAppointment) return;
    const approvedDate = new Date(`${scheduleDate}T${scheduleTime}`);
    if (Number.isNaN(approvedDate.getTime())) {
      toast.error('Select a valid date and time.');
      return;
    }
    const existingEvent = findAppointmentEvent(activeAppointment.id);
    const updated = await updateAppointmentStatus(activeAppointment, 'approved', approvedDate, assignedTo, scheduleDuration);
    if (updated) {
      const eventPayload = {
        title: `${activeAppointment.client?.name || 'Client'} - ${activeAppointment.type}`,
        date: approvedDate.toISOString(),
        type: (activeAppointment.type === 'consultation' ? 'Consultation' :
          activeAppointment.type === 'meeting' ? 'Meeting' :
            activeAppointment.type === 'call' ? 'Conference' : 'Court') as 'Consultation' | 'Meeting' | 'Conference' | 'Court',
        duration: scheduleDuration,
        description: buildAppointmentDescription(activeAppointment.notes, activeAppointment.id),
        matterId: activeAppointment.matterId || undefined,
        location: 'Client appointment'
      };
      if (existingEvent) {
        await updateEvent(existingEvent.id, eventPayload);
        toast.success('Appointment rescheduled.');
      } else {
        await addEvent(eventPayload);
        toast.success('Appointment approved and scheduled.');
      }
      setShowAppointmentModal(false);
      setActiveAppointment(null);
    }
  };

  const handleRejectAppointment = async (appointment: AppointmentItem) => {
    const ok = await confirm({
      title: 'Reject appointment request',
      message: 'Are you sure you want to reject this appointment request?',
      confirmText: 'Reject',
      cancelText: 'Cancel',
      variant: 'danger'
    });
    if (!ok) return;
    const updated = await updateAppointmentStatus(appointment, 'rejected');
    if (updated) {
      toast.success('Appointment rejected.');
    }
  };

  const handleNotifyAppointment = async (appointment: AppointmentItem) => {
    try {
      await api.notifyAppointment(appointment.id);
      toast.success('Client notified.');
    } catch (error) {
      console.error('Failed to notify client', error);
      toast.error('Unable to send notification.');
    }
  };

  const getAppointmentBadge = (status: AppointmentItem['status']) => {
    if (status === 'approved') return 'bg-green-100 text-green-800';
    if (status === 'rejected') return 'bg-red-100 text-red-800';
    if (status === 'cancelled') return 'bg-gray-100 text-gray-600';
    return 'bg-amber-100 text-amber-800';
  };

  const filteredAppointments = appointments.filter(a => {
    if (appointmentFilter === 'all') return true;
    return a.status === appointmentFilter;
  });

  return (
    <div className="p-8 h-full flex flex-col bg-gray-50/50 relative overflow-y-auto">
      <div className="flex justify-between items-center mb-6">
        <div>
          <h1 className="text-3xl font-bold font-sans text-slate-900">{t('cal_title')}</h1>
          <p className="text-sm text-gray-500 mt-1">
            {new Date(currentYear, currentMonth).toLocaleDateString(language, { month: 'long', year: 'numeric' })}
          </p>
        </div>
        <div className="flex gap-2">
          <button onClick={() => {
            if (currentMonth === 0) { setCurrentMonth(11); setCurrentYear(y => y - 1); }
            else { setCurrentMonth(m => m - 1); }
          }} className="p-2 hover:bg-gray-200 rounded text-slate-600">
            &larr;
          </button>
          <button onClick={() => {
            if (currentMonth === 11) { setCurrentMonth(0); setCurrentYear(y => y + 1); }
            else { setCurrentMonth(m => m + 1); }
          }} className="p-2 hover:bg-gray-200 rounded text-slate-600">
            &rarr;
          </button>
        </div>
      </div>

      <div className="flex h-full gap-6">
        {/* Calendar Grid */}
        <div className="flex-1 bg-white rounded-xl shadow-card border border-gray-200 flex flex-col overflow-hidden">
          {/* Header Row */}
          <div className="grid grid-cols-7 border-b border-gray-200 bg-gray-50">
            {daysShort.map((d: string) => (
              <div key={d} className="py-3 text-center text-xs font-bold text-gray-500 uppercase tracking-wide">
                {d}
              </div>
            ))}
          </div>

          {/* Days Grid */}
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

              // Get tasks with deadlines on this day
              const dayTasks = tasks.filter(t => {
                if (!t.dueDate) return false;
                const d = new Date(t.dueDate);
                return d.getDate() === day && d.getMonth() === currentMonth && d.getFullYear() === currentYear;
              });

              return (
                <div
                  key={day}
                  onClick={() => openAddModal(day)}
                  className={`min-h-[100px] p-2 relative hover:bg-blue-50 cursor-pointer transition-colors group flex flex-col gap-1 ${isToday ? 'bg-blue-50/50' : ''}`}
                >
                  <span className={`text-sm font-bold w-7 h-7 flex items-center justify-center rounded-full ${isToday ? 'bg-blue-600 text-white shadow-md' : 'text-gray-700'}`}>
                    {day}
                  </span>

                  {/* Event Indicators */}
                  <div className="flex flex-col gap-1 mt-1">
                    {dayEvents.map(ev => (
                      <div
                        key={ev.id}
                        className={`text-[10px] px-1.5 py-0.5 rounded truncate font-medium group/item relative ${
                          // Check if the event is happening now
                          (() => {
                            const now = Date.now();
                            const start = new Date(ev.date).getTime();
                            const end = start + (ev.duration || 60) * 60 * 1000;
                            const isHappening = now >= start && now <= end;
                            const isPast = end < now;

                            if (isHappening) return 'bg-green-500 text-white animate-pulse ring-2 ring-green-300 z-10';
                            if (isPast) return 'bg-gray-100 text-gray-400 opacity-60 line-through decoration-gray-400';

                            return ev.type === 'Court' ? 'bg-red-100 text-red-700' :
                              ev.type === 'Deadline' ? 'bg-amber-100 text-amber-700' :
                                'bg-blue-100 text-blue-700';
                          })()
                          }`}
                        title={ev.title}
                      >
                        <span className="truncate block">{ev.title}</span>
                        <button
                          onClick={async (e) => {
                            e.stopPropagation();
                            const ok = await confirm({
                              title: 'Delete event',
                              message: `Are you sure you want to delete "${ev.title}"?`,
                              confirmText: 'Delete',
                              cancelText: 'Cancel',
                              variant: 'danger'
                            });
                            if (!ok) return;
                            deleteEvent(ev.id);
                          }}
                          className="absolute right-1 top-0.5 opacity-0 group-hover/item:opacity-100 transition-opacity text-red-600 hover:text-red-800"
                          title="Delete event"
                        >
                          <X className="w-3 h-3" />
                        </button>
                      </div>
                    ))}
                    {/* Task Indicators */}
                    {dayTasks.map(task => (
                      <div
                        key={task.id}
                        className={`text-[10px] px-1.5 py-0.5 rounded truncate font-medium ${task.priority === 'High' ? 'bg-purple-100 text-purple-700' :
                          task.priority === 'Medium' ? 'bg-indigo-100 text-indigo-700' :
                            'bg-gray-100 text-gray-700'
                          }`}
                        title={`Task: ${task.title}`}
                      >
                        <span className="truncate block">{task.title}</span>
                      </div>
                    ))}
                  </div>
                </div>
              )
            })}
          </div>
        </div>

        {/* Sidebar Schedule */}
        <div className="w-80 bg-white rounded-xl shadow-card border border-gray-200 p-6 flex flex-col">
          <div className="pb-4 border-b border-gray-100">
            <div className="flex items-center justify-between">
              <h3 className="font-bold text-lg text-slate-900">Appointment Requests</h3>
              <button
                onClick={loadAppointments}
                className="text-xs text-gray-500 hover:text-slate-700"
              >
                Refresh
              </button>
            </div>
            <div className="flex flex-wrap gap-2 mt-3">
              {[
                { key: 'pending', label: 'Pending' },
                { key: 'approved', label: 'Approved' },
                { key: 'rejected', label: 'Rejected' },
                { key: 'all', label: 'All' }
              ].map(item => (
                <button
                  key={item.key}
                  onClick={() => setAppointmentFilter(item.key as any)}
                  className={`px-2.5 py-1 text-xs rounded-full border ${appointmentFilter === item.key
                    ? 'bg-slate-900 text-white border-slate-900'
                    : 'bg-white text-gray-600 border-gray-200 hover:border-gray-300'}`}
                >
                  {item.label}
                </button>
              ))}
            </div>
            <div className="mt-4 space-y-3 max-h-64 overflow-y-auto pr-1">
              {appointmentsLoading && (
                <p className="text-xs text-gray-400">Loading appointments...</p>
              )}
              {!appointmentsLoading && filteredAppointments.length === 0 && (
                <p className="text-xs text-gray-400">No appointment requests in this view.</p>
              )}
              {filteredAppointments.map(appointment => (
                <div key={appointment.id} className="border border-gray-200 rounded-lg p-3">
                  <div className="flex items-start justify-between gap-2">
                    <div>
                      <p className="text-sm font-semibold text-slate-800">
                        {appointment.client?.name || 'Client'} - {appointment.type}
                      </p>
                      <p className="text-xs text-gray-500 mt-1">
                        Requested {new Date(appointment.requestedDate).toLocaleString('en-US')}
                      </p>
                      {appointment.matterId && (
                        <p className="text-[11px] text-gray-500 mt-1">
                          Matter: {matters.find(m => m.id === appointment.matterId)?.name || appointment.matterId}
                        </p>
                      )}
                    </div>
                    <span className={`px-2 py-0.5 text-[10px] font-bold rounded ${getAppointmentBadge(appointment.status)}`}>
                      {appointment.status}
                    </span>
                  </div>
                  {appointment.notes && (
                    <p className="text-xs text-gray-500 mt-2">{appointment.notes}</p>
                  )}
                  {appointment.assignedTo && (
                    <p className="text-[11px] text-gray-500 mt-2">
                      Assigned to: {employees.find(emp => emp.id === appointment.assignedTo)?.firstName
                        ? `${employees.find(emp => emp.id === appointment.assignedTo)?.firstName} ${employees.find(emp => emp.id === appointment.assignedTo)?.lastName}`
                        : appointment.assignedTo}
                    </p>
                  )}
                  <div className="flex items-center gap-2 mt-3">
                    {appointment.status === 'pending' && (
                      <>
                        <button
                          onClick={() => openScheduleModal(appointment)}
                          className="px-3 py-1 text-xs font-semibold text-white bg-emerald-600 rounded hover:bg-emerald-700"
                        >
                          Approve & Schedule
                        </button>
                        <button
                          onClick={() => handleRejectAppointment(appointment)}
                          className="px-3 py-1 text-xs font-semibold text-red-600 border border-red-200 rounded hover:bg-red-50"
                        >
                          Reject
                        </button>
                      </>
                    )}
                    {appointment.status === 'approved' && (
                      <>
                        <button
                          onClick={() => openScheduleModal(appointment)}
                          className="px-3 py-1 text-xs font-semibold text-white bg-slate-800 rounded hover:bg-slate-900"
                        >
                          Reschedule
                        </button>
                        <button
                          onClick={() => handleNotifyAppointment(appointment)}
                          className="px-3 py-1 text-xs font-semibold text-slate-600 border border-gray-200 rounded hover:bg-gray-50"
                        >
                          Re-notify
                        </button>
                      </>
                    )}
                    {appointment.status === 'approved' && appointment.approvedDate && (
                      <p className="text-xs text-gray-500">
                        Approved for {new Date(appointment.approvedDate).toLocaleString('en-US')}
                      </p>
                    )}
                  </div>
                </div>
              ))}
            </div>
          </div>

          <div className="flex-1 overflow-y-auto pt-4">
            <h3 className="font-bold text-lg text-slate-900 mb-4">{t('upcoming_schedule')}</h3>
            {events.length === 0 ? (
              <div className="flex-1 flex flex-col items-center justify-center text-gray-400 text-sm italic">
                No upcoming events.
              </div>
            ) : (
              <div className="space-y-6 overflow-y-auto pr-2">
                {events
                  .filter(e => {
                    const end = new Date(e.date).getTime() + (e.duration || 60) * 60 * 1000;
                    return end >= Date.now();
                  })
                  .sort((a, b) => new Date(a.date).getTime() - new Date(b.date).getTime())
                  .map(event => (
                    <div key={event.id} className="flex gap-3 relative group">
                      <div className="flex flex-col items-center">
                        <div className="w-2 h-2 rounded-full bg-slate-400 mt-2 group-hover:bg-primary-500 transition-colors"></div>
                        <div className="w-px h-full bg-gray-200 my-1"></div>
                      </div>
                      <div className="pb-4 flex-1">
                        <div className="flex justify-between items-start">
                          <div className="flex-1">
                            <p className="text-sm font-semibold text-gray-800">{event.title}</p>
                            <p className="text-xs text-gray-500 flex items-center gap-1 mt-1">
                              <Clock className="w-3 h-3" />
                              {new Date(event.date).toLocaleDateString() + " " + new Date(event.date).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' })}
                            </p>
                            <span className={`inline-block mt-2 px-2 py-0.5 rounded text-[10px] uppercase font-bold tracking-wide
                         ${(() => {
                                const now = Date.now();
                                const start = new Date(event.date).getTime();
                                const end = start + (event.duration || 60) * 60 * 1000;
                                const isHappening = now >= start && now <= end;
                                if (isHappening) return 'bg-green-500 text-white animate-pulse';
                                return event.type === 'Court' ? 'bg-red-50 text-red-700' :
                                  event.type === 'Deadline' ? 'bg-amber-50 text-amber-700' : 'bg-blue-50 text-blue-700';
                              })()}`}>
                              {(() => {
                                const now = Date.now();
                                const start = new Date(event.date).getTime();
                                const end = start + (event.duration || 60) * 60 * 1000;
                                const isHappening = now >= start && now <= end;
                                return isHappening ? 'Live' : event.type;
                              })()}
                            </span>
                          </div>
                          <button
                            onClick={async () => {
                              const ok = await confirm({
                                title: 'Delete event',
                                message: `Are you sure you want to delete "${event.title}"?`,
                                confirmText: 'Delete',
                                cancelText: 'Cancel',
                                variant: 'danger'
                              });
                              if (!ok) return;
                              deleteEvent(event.id);
                            }}
                            className="opacity-0 group-hover:opacity-100 transition-opacity text-red-500 hover:text-red-700 p-1"
                            title="Delete event"
                          >
                            <Trash className="w-4 h-4" />
                          </button>
                        </div>
                      </div>
                    </div>
                  ))}
              </div>
            )}
            <button onClick={() => openAddModal(currentDate.getDate())} className="mt-4 w-full py-2 border border-dashed border-gray-300 rounded text-sm text-gray-500 hover:border-gray-400 hover:text-gray-700">
              + Quick Add Today
            </button>
          </div>
        </div>
      </div>

      {showModal && (
        <div className="fixed inset-0 bg-black/50 z-50 flex items-center justify-center p-4">
          <div className="bg-white rounded-xl shadow-2xl p-6 w-[480px] max-h-[90vh] overflow-y-auto animate-in fade-in zoom-in-95 duration-200">
            <div className="flex justify-between items-center mb-4 border-b border-gray-100 pb-2">
              <h3 className="font-bold text-lg text-slate-900">Add Event</h3>
              <button onClick={() => setShowModal(false)}><X className="w-5 h-5 text-gray-400 hover:text-gray-600" /></button>
            </div>
            <div className="mb-4 bg-blue-50 px-3 py-2 rounded text-blue-800 text-sm font-medium">
              Date: {selectedDate?.toLocaleDateString()}
            </div>
            <form onSubmit={handleAddEvent} className="space-y-4">
              {/* Event Title */}
              <div>
                <label className="block text-xs font-bold text-gray-500 uppercase mb-1">Event Title *</label>
                <input required className="w-full border border-gray-300 p-2.5 rounded-lg bg-white text-slate-900 focus:ring-2 focus:ring-slate-500 outline-none" placeholder="e.g. Client Meeting" value={newTitle} onChange={e => setNewTitle(e.target.value)} />
              </div>

              {/* Type and Time */}
              <div className="grid grid-cols-2 gap-3">
                <div>
                  <label className="block text-xs font-bold text-gray-500 uppercase mb-1">Type</label>
                  <select className="w-full border border-gray-300 p-2.5 rounded-lg bg-white text-slate-900 focus:ring-2 focus:ring-slate-500 outline-none" value={newType} onChange={e => setNewType(e.target.value)}>
                    <option value="Meeting">Meeting</option>
                    <option value="Court">Court</option>
                    <option value="Deadline">Deadline</option>
                    <option value="Deposition">Deposition</option>
                    <option value="Consultation">Consultation</option>
                    <option value="Filing">Filing</option>
                    <option value="Hearing">Hearing</option>
                    <option value="Trial">Trial</option>
                    <option value="Conference">Conference</option>
                    <option value="Other">Other</option>
                  </select>
                </div>
                <div>
                  <label className="block text-xs font-bold text-gray-500 uppercase mb-1">Time</label>
                  <input type="time" className="w-full border border-gray-300 p-2.5 rounded-lg bg-white text-slate-900 focus:ring-2 focus:ring-slate-500 outline-none" value={newTime} onChange={e => setNewTime(e.target.value)} />
                </div>
              </div>

              {/* Location (Optional) */}
              <div>
                <label className="block text-xs font-bold text-gray-500 uppercase mb-1">Location <span className="text-gray-400 font-normal">(optional)</span></label>
                <input className="w-full border border-gray-300 p-2.5 rounded-lg bg-white text-slate-900 focus:ring-2 focus:ring-slate-500 outline-none" placeholder="e.g. Conference Room A, Courthouse, Zoom link..." value={newLocation} onChange={e => setNewLocation(e.target.value)} />
              </div>

              {/* Duration and Recurrence */}
              <div className="grid grid-cols-2 gap-3">
                <div>
                  <label className="block text-xs font-bold text-gray-500 uppercase mb-1">Duration</label>
                  <select className="w-full border border-gray-300 p-2.5 rounded-lg bg-white text-slate-900 focus:ring-2 focus:ring-slate-500 outline-none" value={newDuration} onChange={e => setNewDuration(Number(e.target.value))}>
                    <option value={15}>15 minutes</option>
                    <option value={30}>30 minutes</option>
                    <option value={60}>1 hour</option>
                    <option value={90}>1.5 hours</option>
                    <option value={120}>2 hours</option>
                    <option value={180}>3 hours</option>
                    <option value={240}>4 hours</option>
                    <option value={480}>All day</option>
                  </select>
                </div>
                <div>
                  <label className="block text-xs font-bold text-gray-500 uppercase mb-1">Recurrence <span className="text-gray-400 font-normal">(optional)</span></label>
                  <select className="w-full border border-gray-300 p-2.5 rounded-lg bg-white text-slate-900 focus:ring-2 focus:ring-slate-500 outline-none" value={newRecurrence} onChange={e => setNewRecurrence(e.target.value)}>
                    <option value="none">No repeat</option>
                    <option value="daily">Daily</option>
                    <option value="weekly">Weekly</option>
                    <option value="monthly">Monthly</option>
                  </select>
                </div>
              </div>

              {/* Reminder */}
              <div>
                <label className="block text-xs font-bold text-gray-500 uppercase mb-1">Reminder</label>
                <select className="w-full border border-gray-300 p-2.5 rounded-lg bg-white text-slate-900 focus:ring-2 focus:ring-slate-500 outline-none" value={newReminderMinutes} onChange={e => setNewReminderMinutes(Number(e.target.value))}>
                  <option value={0}>No reminder</option>
                  <option value={5}>5 minutes before</option>
                  <option value={15}>15 minutes before</option>
                  <option value={30}>30 minutes before</option>
                  <option value={60}>1 hour before</option>
                  <option value={120}>2 hours before</option>
                  <option value={1440}>1 day before</option>
                </select>
              </div>

              {/* Description (Optional) */}
              <div>
                <label className="block text-xs font-bold text-gray-500 uppercase mb-1">Description <span className="text-gray-400 font-normal">(optional)</span></label>
                <textarea
                  className="w-full border border-gray-300 p-2.5 rounded-lg bg-white text-slate-900 focus:ring-2 focus:ring-slate-500 outline-none resize-none"
                  rows={3}
                  placeholder="Add notes or details about this event..."
                  value={newDescription}
                  onChange={e => setNewDescription(e.target.value)}
                />
              </div>

              {/* Buttons */}
              <div className="flex gap-2 pt-2">
                <button type="button" onClick={() => setShowModal(false)} className="flex-1 py-2 text-gray-600 font-bold hover:bg-gray-100 rounded-lg">Cancel</button>
                <button type="submit" className="flex-1 py-2 bg-slate-900 text-white rounded-lg font-bold hover:bg-slate-800 shadow-lg">Save</button>
              </div>
            </form>
          </div>
        </div>
      )}

      {showAppointmentModal && activeAppointment && (
        <div className="fixed inset-0 bg-black/50 z-50 flex items-center justify-center p-4">
          <div className="bg-white rounded-xl shadow-2xl p-6 w-[480px] max-h-[90vh] overflow-y-auto animate-in fade-in zoom-in-95 duration-200">
            <div className="flex justify-between items-center mb-4 border-b border-gray-100 pb-2">
              <div>
                <p className="text-xs text-gray-500 uppercase">{activeAppointment.status === 'approved' ? 'Reschedule Appointment' : 'Schedule Appointment'}</p>
                <h3 className="font-bold text-lg text-slate-900">{activeAppointment.client?.name || 'Client'} - {activeAppointment.type}</h3>
              </div>
              <button onClick={() => setShowAppointmentModal(false)}>
                <X className="w-5 h-5 text-gray-400 hover:text-gray-600" />
              </button>
            </div>
            <div className="mb-4 bg-slate-50 px-3 py-2 rounded text-slate-700 text-sm">
              Requested: {new Date(activeAppointment.requestedDate).toLocaleString('en-US')}
            </div>
            <div className="space-y-4">
              <div className="grid grid-cols-2 gap-3">
                <div>
                  <label className="block text-xs font-bold text-gray-500 uppercase mb-1">Date</label>
                  <input
                    type="date"
                    className="w-full border border-gray-300 p-2.5 rounded-lg bg-white text-slate-900 focus:ring-2 focus:ring-slate-500 outline-none"
                    value={scheduleDate}
                    onChange={(e) => setScheduleDate(e.target.value)}
                  />
                </div>
                <div>
                  <label className="block text-xs font-bold text-gray-500 uppercase mb-1">Time</label>
                  <input
                    type="time"
                    className="w-full border border-gray-300 p-2.5 rounded-lg bg-white text-slate-900 focus:ring-2 focus:ring-slate-500 outline-none"
                    value={scheduleTime}
                    onChange={(e) => setScheduleTime(e.target.value)}
                  />
                </div>
              </div>
              <div className="grid grid-cols-2 gap-3">
                <div>
                  <label className="block text-xs font-bold text-gray-500 uppercase mb-1">Duration</label>
                  <select
                    className="w-full border border-gray-300 p-2.5 rounded-lg bg-white text-slate-900 focus:ring-2 focus:ring-slate-500 outline-none"
                    value={scheduleDuration}
                    onChange={(e) => setScheduleDuration(Number(e.target.value))}
                  >
                    <option value={15}>15 minutes</option>
                    <option value={30}>30 minutes</option>
                    <option value={45}>45 minutes</option>
                    <option value={60}>1 hour</option>
                    <option value={90}>1.5 hours</option>
                    <option value={120}>2 hours</option>
                  </select>
                </div>
                <div>
                  <label className="block text-xs font-bold text-gray-500 uppercase mb-1">Assigned To</label>
                  <select
                    className="w-full border border-gray-300 p-2.5 rounded-lg bg-white text-slate-900 focus:ring-2 focus:ring-slate-500 outline-none"
                    value={assignedTo}
                    onChange={(e) => setAssignedTo(e.target.value)}
                  >
                    <option value="">Unassigned</option>
                    {employees.map(emp => (
                      <option key={emp.id} value={emp.id}>
                        {emp.firstName} {emp.lastName}
                      </option>
                    ))}
                  </select>
                </div>
              </div>
              {activeAppointment.notes && (
                <div className="bg-gray-50 border border-gray-200 rounded-lg p-3 text-sm text-gray-600">
                  {activeAppointment.notes}
                </div>
              )}
              <div className="flex gap-2 pt-2">
                <button
                  type="button"
                  onClick={() => setShowAppointmentModal(false)}
                  className="flex-1 py-2 text-gray-600 font-bold hover:bg-gray-100 rounded-lg"
                >
                  Cancel
                </button>
                <button
                  type="button"
                  onClick={handleApproveAppointment}
                  className="flex-1 py-2 bg-emerald-600 text-white rounded-lg font-bold hover:bg-emerald-700 shadow-lg"
                >
                  {activeAppointment.status === 'approved' ? 'Save Changes' : 'Approve & Schedule'}
                </button>
              </div>
            </div>
          </div>
        </div>
      )}
    </div>
  );
};

export default CalendarView;
