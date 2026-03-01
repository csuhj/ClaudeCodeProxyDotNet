import { Component } from '@angular/core';
import { DatePipe } from '@angular/common';
import { HourlyStats } from './components/hourly-stats/hourly-stats';
import { DailyStats } from './components/daily-stats/daily-stats';

@Component({
  selector: 'app-root',
  imports: [DatePipe, HourlyStats, DailyStats],
  templateUrl: './app.html',
  styleUrl: './app.scss',
})
export class App {
  readonly now = new Date();
}
